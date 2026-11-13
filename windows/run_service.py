"""Windows-only manual service host. All model/cache/state files live beside the app."""
import json
import logging
import msvcrt
import os
from pathlib import Path
import sys
import time
from http.server import ThreadingHTTPServer

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
os.chdir(ROOT)
os.environ.update(HF_HOME=str(ROOT / "models/huggingface"), HF_HUB_OFFLINE="1",
                  HF_HUB_DISABLE_XET="1", HF_HUB_DISABLE_IMPLICIT_TOKEN="1",
                  OMP_NUM_THREADS="4", PYTHONUTF8="1",
                  CUDA_CACHE_PATH=str(ROOT / 'cache/cuda'),
                  TORCH_HOME=str(ROOT / 'models/torch'),
                  TEMP=str(ROOT / 'cache/tmp'), TMP=str(ROOT / 'cache/tmp'))
STATE = ROOT / "state"
STATE.mkdir(exist_ok=True)


def main():
    # A file lock works across the interactive desktop and an SSH login session.
    lock = (STATE / "service.lock").open("a+b")
    lock.seek(0)
    lock.write(b"0")
    lock.flush()
    lock.seek(0)
    try:
        msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
    except OSError:
        print("动作服务已在运行。", flush=True)
        return
    state = STATE / "service.json"
    def record(status):
        state.write_text(json.dumps(dict(pid=os.getpid(), status=status,
                         root=str(ROOT), updated=time.time())), encoding="utf-8")
    record("loading")
    server = None
    jobs = hosted = None
    try:
        from service.dip_provider import DiPProvider
        from service.server import Jobs, make_handler
        from service.ardy_hosted import HostedArdyProvider
        provider = DiPProvider()
        print("正在加载本地 DiP 动作模型…", flush=True)
        provider.load()
        jobs = Jobs(provider, ROOT / "cache")
        hosted = Jobs(HostedArdyProvider(), ROOT / "cache/ardy")
        # The same manual start/stop entry owns both models and one GPU queue.
        from service.streamtalk_provider import StreamTalkProvider
        from service.speech_jobs import SpeechJobs
        speech = SpeechJobs(StreamTalkProvider(), jobs.pool)
        if (ROOT / "models/streamtalk/SMPLX_NEUTRAL_2020.npz").is_file():
            def warm_speech():
                try: speech.provider.load()
                except Exception:
                    speech.provider.state = "error"
                    logging.exception("StreamTalk warmup failed; DiP remains available")
            jobs.pool.submit(warm_speech)
        server = ThreadingHTTPServer(("127.0.0.1", 8787), make_handler(jobs, hosted, speech))
        logging.basicConfig(level=logging.INFO, format="%(asctime)s %(message)s")
        record("ready")
        print("动作服务已就绪：http://127.0.0.1:8787；双击关闭入口可释放显存。", flush=True)
        server.serve_forever()
    finally:
        if server:
            server.server_close()
        for queue in (jobs, hosted):
            if queue:
                queue.pool.shutdown(wait=False, cancel_futures=True)
        state.unlink(missing_ok=True)
        lock.close()


if __name__ == "__main__":
    (ROOT / 'logs').mkdir(exist_ok=True)
    (ROOT / 'cache/tmp').mkdir(parents=True, exist_ok=True)
    sys.stdout = (ROOT / 'logs/service.log').open('w', encoding='utf-8', buffering=1)
    sys.stderr = (ROOT / 'logs/service-error.log').open('w', encoding='utf-8', buffering=1)
    main()
