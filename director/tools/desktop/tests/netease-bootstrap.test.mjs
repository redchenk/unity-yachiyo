import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { EventEmitter } from 'node:events';
import { createKeyBootstrap } from '../netease-bootstrap.cjs';
import { createNeteaseClient } from '../netease-client.mjs';

const key = { publicKey: Buffer.alloc(32, 1).toString('base64'), sk: 'test-only', version: '1' };
test('fresh Windows cache initializes once for concurrent calls and survives restart', async () => {
  const cacheDir = fs.mkdtempSync(path.join(os.tmpdir(), 'netease-init-')); let calls = 0;
  try {
    const options = { cacheDir, getDeviceId: () => 'test-device', getKey: async () => { calls++; await new Promise(r => setTimeout(r, 10)); return key; } };
    const init = createKeyBootstrap(options);
    const results = await Promise.all([init(), init(), init()]);
    assert.equal(calls, 1); assert(results.every(x => x.deviceId === 'test-device'));
    assert.equal(fs.existsSync(path.join(cacheDir, 'xeapi_public_key.partial')), false);
    await createKeyBootstrap(options)(); assert.equal(calls, 1);
  } finally { fs.rmSync(cacheDir, { recursive: true, force: true }); }
});
test('corrupt cache and failed network recover without recording invalid keys or private errors', async () => {
  const cacheDir = fs.mkdtempSync(path.join(os.tmpdir(), 'netease-retry-')); let calls = 0;
  try {
    fs.writeFileSync(path.join(cacheDir, 'xeapi_public_key'), '{');
    const init = createKeyBootstrap({ cacheDir, getDeviceId: () => 'test-device', getKey: async () => { if (++calls === 1) throw Error('sensitive provider headers'); return key; } });
    await assert.rejects(init(), /初始化失败/);
    assert.equal(fs.readFileSync(path.join(cacheDir, 'xeapi_public_key'), 'utf8'), '{');
    assert.deepEqual(await init(), { ...key, deviceId: 'test-device' });
    assert.equal(calls, 2);
  } finally { fs.rmSync(cacheDir, { recursive: true, force: true }); }
});
test('hung SDK worker is terminated, retries start fresh, and close removes owned process', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'netease-worker-')); const children = [];
  const client = createNeteaseClient(dataDir, { timeoutMs: 25, forkImpl: (file, args, options) => {
    const worker = new EventEmitter(); worker.killed = false;
    worker.send = ({ id }) => { if (children.length > 1) queueMicrotask(() => worker.emit('message', { id, result: { body: { code: 200 } } })); };
    worker.kill = () => { worker.killed = true; queueMicrotask(() => worker.emit('exit', 0)); };
    assert.equal(options.env.TEMP, path.join(dataDir, 'netease-cache')); assert.equal(options.windowsHide, true);
    children.push(worker); return worker;
  } });
  try {
    await assert.rejects(client.call('cloudsearch', {}), /超时/); assert(children[0].killed);
    assert.equal((await client.call('cloudsearch', {})).body.code, 200);
    await client.close(); assert(children[1].killed);
    await assert.rejects(client.call('cloudsearch', {}), /closed/);
  } finally { await client.close(); fs.rmSync(dataDir, { recursive: true, force: true }); }
});
