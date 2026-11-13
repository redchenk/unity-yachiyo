"""Bounded, cancellable audio jobs sharing the existing single-GPU executor."""
import base64
import binascii
import logging
import secrets
import threading
import time
import numpy as np


class SpeechJobs:
    def __init__(self, provider, pool):
        self.provider, self.pool = provider, pool
        self.lock = threading.Lock()
        self.jobs = {}

    def submit(self, raw):
        if not isinstance(raw, dict) or raw.get("sampleRate") != 16000:
            raise ValueError("16 kHz mono PCM16 required")
        seed = raw.get("seed", 7)
        if type(seed) is not int or not 0 <= seed <= 2147483647:
            raise ValueError("Invalid seed")
        try:
            pcm = base64.b64decode(raw.get("pcm16", ""), validate=True)
        except (binascii.Error, TypeError):
            raise ValueError("Invalid PCM") from None
        if len(pcm) % 2 or not 1600 <= len(pcm) <= 16000 * 2 * 60:
            raise ValueError("Audio must be 0.05–60 seconds")
        samples = np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768
        with self.lock:
            now = time.monotonic()
            for key, job in list(self.jobs.items()):
                if now-job["created"] > 180 or (len(self.jobs) >= 8 and job["status"] in ("done", "failed", "cancelled")):
                    job["cancel"].set(); del self.jobs[key]
            if sum(j["status"] in ("queued", "running") for j in self.jobs.values()) >= 2:
                raise OverflowError("Speech motion queue full")
            key = secrets.token_hex(12)
            self.jobs[key] = dict(id=key, status="queued", created=now, cancel=threading.Event(),
                                  frames=[], motion=None, error="", seconds=0.)
            self.pool.submit(self._run, key, samples, seed)
        return {"id": key, "status": "queued"}

    def _run(self, key, samples, seed):
        with self.lock:
            job = self.jobs.get(key)
            if not job or job["cancel"].is_set(): return
            job["status"] = "running"
        start = time.monotonic()
        try:
            for motion in self.provider.generate(samples, seed, job["cancel"].is_set):
                with self.lock:
                    if job["cancel"].is_set(): return
                    if motion["startFrame"] != len(job["frames"]):
                        raise ValueError("Discontinuous speech motion")
                    job["frames"].extend(motion["frames"])
                    job["motion"] = {k:v for k,v in motion.items() if k != "frames"}
                    job["seconds"] = round(time.monotonic()-start,4)
            with self.lock:
                if not job["cancel"].is_set(): job["status"] = "done"
        except Exception as error:
            logging.exception("StreamTalk inference failed")
            with self.lock:
                if job["cancel"].is_set(): return
                job["status"] = "failed"
                job["error"] = ("StreamTalk 缺少官方模型或检索数据。" if isinstance(error, FileNotFoundError)
                                else "StreamTalk 推理失败（"+type(error).__name__+"），请检查动作服务日志。")
            self.provider.state = "error"

    def get(self, key, after=0):
        with self.lock:
            job = self.jobs.get(key)
            if not job: return None
            if after < 0 or after > len(job["frames"]): raise ValueError("Invalid frame cursor")
            # At most 52 frames per poll. This bounds both network and Unity input.
            size = 53 if len(job["frames"])-after == 53 else 52
            frames = job["frames"][after:after+size]
            result = {k:job[k] for k in ("id", "status", "error", "seconds")}
            result["availableFrames"] = len(job["frames"])
            if frames:
                result["motion"] = dict(job["motion"], frames=frames, startFrame=after)
            return result

    def cancel(self, key):
        with self.lock:
            job = self.jobs.pop(key, None)
            if job:
                job["cancel"].set(); job["status"] = "cancelled"
                job["frames"].clear()
