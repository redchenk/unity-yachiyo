"""Single-GPU job service. Bind loopback and use an SSH tunnel to the Unity client."""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import logging
import math
import os
from pathlib import Path
import re
import threading
import time
from urllib.parse import urlparse, parse_qs

from .motion import convert, write_motion

ROOT = Path(__file__).resolve().parents[1]
TRANSLATIONS = {
    "向我挥挥手": "A person waves hello with their right hand.",
    "你好": "A person waves hello with their right hand.",
    "站着": "A person stands still with arms relaxed at their sides.",
    "挥手": "A person waves hello with their right hand.",
    "鞠躬": "A person bows politely.",
    "跳舞": "A person does a cheerful dance in place.",
    "伸懒腰": "A person stretches both arms overhead.",
    "转一圈": "A person turns around once in place.",
    "走过来": "A person walks forward slowly.",
    "向前走": "A person walks forward slowly.",
    "坐下": "A person sits down.",
    "点头": "A person nods their head.",
}


def normalize_request(data):
    if not isinstance(data, dict): raise ValueError("Request must be an object")
    prompt = data.get("prompt", "")
    if not isinstance(prompt, str) or not prompt.strip() or len(prompt) > 500: raise ValueError("Prompt must contain 1–500 characters")
    prompt = prompt.strip()
    if re.search(r"[\u4e00-\u9fff]", prompt):
        if prompt not in TRANSLATIONS: raise ValueError("请使用英文动作描述，或预设中文：跳舞、伸懒腰、向前走、挥手、鞠躬、转一圈、点头。")
        prompt = TRANSLATIONS[prompt]
    duration, steps, seed = float(data.get("duration", 4)), int(data.get("steps", 30)), int(data.get("seed", 7))
    if not math.isfinite(duration) or not 1 <= duration <= 10: raise ValueError("Duration must be 1–10 seconds")
    if not 4 <= steps <= 100 or not 0 <= seed <= 2147483647: raise ValueError("Invalid steps or seed")
    return {"prompt": prompt, "duration": duration, "steps": steps, "seed": seed}


class KimodoProvider:
    name = "kimodo"
    def __init__(self, model):
        self.checkpoint_name = model
        self.encoder_mode = os.environ.get("YACHIYO_ENCODER", "cpu").lower()
        if self.encoder_mode not in ("nf4", "cpu", "api"):
            raise ValueError("YACHIYO_ENCODER must be nf4, cpu or api")
        self.encoder_url = os.environ.get("TEXT_ENCODER_URL", "").strip()
        if self.encoder_mode == "api":
            url = urlparse(self.encoder_url)
            if url.scheme not in ("http", "https") or not url.hostname or url.username or url.password:
                raise ValueError("API mode requires TEXT_ENCODER_URL for the official compatible LLM2Vec service")
        self.quantized = self.encoder_mode == "nf4"
        names = {"nf4": "NF4 text encoder", "cpu": "BF16 CPU text encoder", "api": "Remote LLM2Vec encoder"}
        self.model_name = model + " / " + names[self.encoder_mode]
        # Different remote deployments must not silently reuse one another's motion cache.
        self.cache_identity = self.model_name + (hashlib.sha256(self.encoder_url.encode()).hexdigest() if self.encoder_mode == "api" else "")
        self.model, self.state = None, "unloaded"
        if self.encoder_mode != "api":
            from huggingface_hub import get_token
            if not get_token(): self.state = "awaiting_hf_login"
    def generate(self, request):
        import torch
        from kimodo import load_model
        from kimodo.tools import seed_everything
        if not torch.cuda.is_available(): raise RuntimeError("CUDA GPU unavailable")
        if self.model is None:
            if self.encoder_mode != "api":
                from huggingface_hub import get_token, HfApi
                token = get_token()
                if not token:
                    self.state = "awaiting_hf_login"
                    raise PermissionError("HF login required")
                HfApi(token=token).auth_check(repo_id="meta-llama/Meta-Llama-3-8B-Instruct",repo_type="model")
            self.state = "loading"
            os.environ.setdefault("TEXT_ENCODER_DEVICE", "cpu")
            os.environ.setdefault("TEXT_ENCODER_MODE", "local")
            encoder = None
            if self.encoder_mode == "api":
                from kimodo.model.text_encoder_api import TextEncoderAPI
                encoder = TextEncoderAPI(self.encoder_url)
            elif self.quantized:
                from .encoder import QuantizedEncoder
                encoder = QuantizedEncoder()
            self.model = load_model(self.checkpoint_name, device="cuda", text_encoder=encoder)
            self.state = "ready"
        seed_everything(request["seed"])
        self.state = "ready"
        with torch.inference_mode():
            output = self.model(request["prompt"], int(request["duration"]*self.model.fps),
                                num_denoising_steps=request["steps"], num_samples=1,
                                post_processing=True, return_numpy=True)
        skeleton = self.model.output_skeleton
        return convert(output, skeleton.bone_order_names, fps=float(self.model.fps),
                       source=self.model_name, prompt=request["prompt"],
                       hip_height=float(skeleton.neutral_joints[skeleton.root_idx, 1]))


class ReplayProvider:
    """Explicit transport test mode; never pretends a recorded sample was newly generated."""
    name, state, model_name = "replay", "ready", "Official recorded sample (not generation)"
    def __init__(self, path): self.path = path
    def generate(self, request):
        motion = json.loads(self.path.read_text())
        motion["source"] = "Kimodo 官方样例回放（非实时生成）"
        return motion


class Jobs:
    def __init__(self, provider, cache):
        self.provider, self.cache, self.lock = provider, cache, threading.Lock()
        cache.mkdir(parents=True, exist_ok=True)
        self.jobs = {}
        self.pool = ThreadPoolExecutor(max_workers=1)
    def submit(self, request):
        key = hashlib.sha256(json.dumps([1, self.provider.name, getattr(self.provider, "cache_identity", self.provider.model_name), request], sort_keys=True).encode()).hexdigest()[:24]
        path = self.cache / (key + ".json")
        with self.lock:
            if key in self.jobs and self.jobs[key]["status"] != "failed":
                if self.jobs[key]["status"]=="done":self.jobs[key]["cached"]=True
                return dict(self.jobs[key])
            if path.exists():
                self.jobs[key] = {"id": key, "status": "done", "cached": True}
                return dict(self.jobs[key])
            if sum(j["status"] in ("running", "queued") for j in self.jobs.values()) >= 2: raise OverflowError("GPU 忙碌，请稍后重试。")
            for old in list(self.jobs):
                if len(self.jobs) >= 64 and self.jobs[old]["status"] in ("done", "failed"): del self.jobs[old]
            self.jobs[key] = {"id": key, "status": "queued", "cached": False}
            self.pool.submit(self.run, key, request)
            return dict(self.jobs[key])
    def run(self, key, request):
        with self.lock: self.jobs[key]["status"] = "running"
        start = time.monotonic()
        try:
            result = self.provider.generate(request)
            write_motion(self.cache / (key+".json"), result)
            with self.lock: self.jobs[key].update(status="done", seconds=round(time.monotonic()-start, 3))
            self.provider.state="ready"
            logging.info("Motion job %s finished in %.2fs", key, time.monotonic()-start)
        except Exception as error:
            logging.error("Motion job %s failed: %s", key, type(error).__name__)
            # Return an actionable message without exposing tokens, paths or HTTP query strings.
            message = "模型加载或生成失败（"+type(error).__name__+"）。请检查模型授权、剩余内存和服务环境。"
            if isinstance(error, PermissionError) or type(error).__name__ == "GatedRepoError":
                message = "请先获得 Llama 3 模型访问权限，并在笔记本登录 Hugging Face。"
            with self.lock: self.jobs[key].update(status="failed", error=message)
            self.provider.state = "awaiting_hf_login" if isinstance(error, PermissionError) else "awaiting_model_approval" if type(error).__name__ == "GatedRepoError" else "error"
    def get(self, key):
        with self.lock: return dict(self.jobs[key]) if key in self.jobs else None


def make_handler(jobs, hosted=None, speech=None):
    from .streams import Streams
    streams=Streams(jobs.provider,jobs.pool) if getattr(jobs.provider,"supports_stream",False) else None
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args): pass
        def send(self, status, data):
            payload = data if isinstance(data, bytes) else json.dumps(data, ensure_ascii=False, allow_nan=False).encode()
            self.send_response(status); self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(payload))); self.end_headers()
            try: self.wfile.write(payload)
            except (BrokenPipeError, ConnectionResetError): pass
        def do_GET(self):
            path = urlparse(self.path).path
            if speech and path == "/speech/health":
                return self.send(200, {"provider": "streamtalk", "state": speech.provider.state,
                                      "model": speech.provider.model_name, "stats": speech.provider.stats})
            match = re.fullmatch(r"/speech/jobs/([a-f0-9]{24})", path)
            if speech and match:
                try:
                    after = int(parse_qs(urlparse(self.path).query).get("after", ["0"])[0])
                    job = speech.get(match[1], after)
                    return self.send(200 if job else 404, job or {"error": "Speech job expired"})
                except ValueError: return self.send(400, {"error": "Invalid frame cursor"})
            active=hosted if hosted and path.startswith('/ardy/') else jobs
            if active is hosted:path=path[5:]
            if path == "/health":
                return self.send(200, {"provider": active.provider.name, "state": active.provider.state, "model": active.provider.model_name, "streaming": active is jobs and streams is not None})
            match = re.fullmatch(r"/jobs/([a-f0-9]{24})(/motion)?", path)
            if not match: return self.send(404, {"error": "Not found"})
            job = active.get(match[1])
            if not job: return self.send(404, {"error": "Job not found"})
            if not match[2]: return self.send(200, job)
            if job["status"] != "done": return self.send(409, {"error": "Motion not ready"})
            return self.send(200, (active.cache/(match[1]+".json")).read_bytes())
        def do_DELETE(self):
            match=re.fullmatch(r"/speech/jobs/([a-f0-9]{24})", self.path)
            if speech and match:
                speech.cancel(match[1]); return self.send(200, {"cancelled": True})
            match=re.fullmatch(r"/streams/([a-f0-9]{24})",self.path)
            if streams and match:
                streams.close(match[1]);return self.send(200,{"closed":True})
            return self.send(404,{"error":"Not found"})
        def do_POST(self):
            path=self.path
            if speech and path == "/speech/jobs":
                try:
                    length = int(self.headers.get("Content-Length", 0))
                    if not 0 < length <= 2600000: return self.send(413, {"error": "Audio request too large"})
                    return self.send(202, speech.submit(json.loads(self.rfile.read(length))))
                except OverflowError: return self.send(429, {"error": "Speech motion service busy"})
                except (ValueError, TypeError, UnicodeError): return self.send(400, {"error": "Expected 16 kHz mono PCM16, 0.05–60 seconds"})
            active=hosted if hosted and path.startswith('/ardy/') else jobs
            if active is hosted:path=path[5:]
            if streams and self.path.startswith("/streams"):
                try:
                    length=int(self.headers.get("Content-Length",0))
                    if not 0<length<=8192:return self.send(413,{"error":"Request too large"})
                    raw=json.loads(self.rfile.read(length))
                    if self.path=="/streams":
                        seed=raw.get("seed",7)
                        if type(seed)!=int or not 0<=seed<=2147483647:raise ValueError()
                        return self.send(201,streams.create(seed))
                    match=re.fullmatch(r"/streams/([a-f0-9]{24})/step",self.path)
                    if not match:return self.send(404,{"error":"Not found"})
                    request=normalize_request(raw)
                    for field in ("sequence","revision"):
                        if type(raw.get(field))!=int or not 0<=raw[field]<=2147483647:raise ValueError()
                        request[field]=raw[field]
                    return self.send(200,streams.step(match[1],request))
                except KeyError:return self.send(404,{"error":"Motion session expired"})
                except (ValueError,TypeError):return self.send(409,{"error":"Invalid or outdated motion request"})
                except OverflowError:return self.send(429,{"error":"Motion service busy"})
                except Exception as error:
                    logging.error("Stream inference failed: %s",type(error).__name__)
                    return self.send(503,{"error":"Motion inference unavailable"})
            if path != "/jobs": return self.send(404, {"error": "Not found"})
            try:
                length = int(self.headers.get("Content-Length", 0))
                if not 0 < length <= 8192: return self.send(413, {"error": "Request too large or empty"})
                data = normalize_request(json.loads(self.rfile.read(length)))
                return self.send(202, active.submit(data))
            except OverflowError as error: return self.send(429, {"error": str(error)})
            except (ValueError, TypeError, UnicodeError): return self.send(400, {"error": "Invalid prompt, duration, steps or seed; use English or a supported Chinese preset."})
    return Handler


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8787)
    parser.add_argument("--provider", choices=["kimodo", "dip", "replay"], default="kimodo")
    parser.add_argument("--model", default="Kimodo-SOMA-RP-v1.2")
    parser.add_argument("--sample", type=Path, default=ROOT/"UnityProject/Assets/StreamingAssets/Motions/kimodo-official.json")
    parser.add_argument("--cache", type=Path, default=ROOT/"service/cache")
    args = parser.parse_args()
    if args.provider=="dip":
        from .dip_provider import DiPProvider
        provider=DiPProvider()
    else:provider = KimodoProvider(args.model) if args.provider == "kimodo" else ReplayProvider(args.sample)
    jobs = Jobs(provider, args.cache)
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(message)s")
    from .ardy_hosted import HostedArdyProvider
    hosted=Jobs(HostedArdyProvider(),args.cache/'ardy')
    server = ThreadingHTTPServer((args.host, args.port), make_handler(jobs,hosted))
    logging.info("%s service at %s:%s", args.provider, args.host, args.port)
    try: server.serve_forever()
    except KeyboardInterrupt: pass
    finally: server.server_close(); jobs.pool.shutdown(wait=True); hosted.pool.shutdown(wait=True)

if __name__ == "__main__": main()
