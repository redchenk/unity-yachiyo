import fs from 'node:fs';
import path from 'node:path';
import { fork } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// The upstream SDK keeps its XEAPI key in os.tmpdir(). Give its owned worker a
// private, persistent cache instead of depending on another app's temp files.
export function createNeteaseClient(dataDir, { forkImpl = fork, timeoutMs = 25_000 } = {}) {
  const cache = path.join(dataDir, 'netease-cache');
  const pending = new Map();
  let child, nextId = 0, closed = false;
  function rejectPending(message) {
    for (const request of pending.values()) { clearTimeout(request.timer); request.reject(new Error(message)); }
    pending.clear();
  }
  function ensureWorker() {
    if (closed) throw new Error('Music service is closed');
    if (child) return child;
    fs.mkdirSync(cache, { recursive: true, mode: 0o700 });
    const worker = forkImpl(fileURLToPath(new URL('./netease-worker.cjs', import.meta.url)), [], {
      cwd: cache, windowsHide: true, stdio: ['ignore', 'ignore', 'ignore', 'ipc'],
      env: { ...process.env, TMP: cache, TEMP: cache, TMPDIR: cache }
    });
    child = worker;
    worker.on('message', message => {
      if (child !== worker) return;
      const request = pending.get(message?.id); if (!request) return;
      clearTimeout(request.timer); pending.delete(message.id);
      if (message.error) request.reject(new Error(message.error)); else request.resolve(message.result);
    });
    const failed = () => {
      if (child !== worker) return;
      child = null; rejectPending('网易云连接中断，请重试。');
    };
    worker.once('error', failed); worker.once('exit', failed);
    return worker;
  }
  return {
    call(name, params) {
      return new Promise((resolve, reject) => {
        const worker = ensureWorker(), id = ++nextId;
        const timer = setTimeout(() => {
          if (child !== worker) return;
          child = null; rejectPending('网易云连接超时，请检查网络后重试。'); worker.kill();
        }, timeoutMs);
        pending.set(id, { resolve, reject, timer });
        worker.send({ id, name, params }, error => {
          if (!error || !pending.has(id)) return;
          clearTimeout(timer); pending.delete(id); reject(new Error('网易云连接中断，请重试。'));
        });
      });
    },
    async close() {
      closed = true; const worker = child; child = null; rejectPending('Music service is closed');
      if (!worker) return;
      await new Promise(resolve => { const timer = setTimeout(resolve, 2000); worker.once('exit', () => { clearTimeout(timer); resolve(); }); worker.kill(); });
    }
  };
}
