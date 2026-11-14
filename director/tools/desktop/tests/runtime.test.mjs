import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { fork } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { studioDataDirectory } from '../../runtime/paths.mjs';
import { createServiceManager } from '../services.mjs';
import { startDesktopServer } from '../server.mjs';

const root = fileURLToPath(new URL('../../../', import.meta.url));
const makeManager = dataDir => createServiceManager({ moduleRoot: root, resourceRoot: root, dataDir, fork: (file, args, options) => fork(file, args, { ...options, env: { ...options.env, YACHIYO_MEMORY_AUTOSTART_MANAGED_MILVUS: '1' }, stdio: ['ignore', 'pipe', 'pipe', 'ipc'] }) });
const post = async (base, route, input) => {
  const response = await fetch(base + route, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) });
  const payload = await response.json();
  assert.equal(response.ok, true, JSON.stringify(payload));
  return payload;
};

test('motion gateway isolates desktop and laptop routes and rejects arbitrary backends', async () => {
  const dataDir=fs.mkdtempSync(path.join(os.tmpdir(),'yachiyo-motion-routes-')), requests=[];
  const runtime=await startDesktopServer({resourceRoot:root,dataDir,port:0,services:{ensure:async()=>{throw Error('not used');}},fetchImpl:async(url,options)=>{
    requests.push({url,method:options.method});return new Response('{}',{headers:{'Content-Type':'application/json'}});
  }});
  try {
    const id='a'.repeat(24);
    for(const [backend,port] of [['desktop',28787],['windows',18787],['ubuntu',8787]]) {
      await post(runtime.origin,`/api/speech-motion/jobs?backend=${backend}`,{sampleRate:16000,pcm16:'AAA='});
      const deleted=await fetch(`${runtime.origin}/api/speech-motion/jobs/${id}?backend=${backend}`,{method:'DELETE',headers:{'Content-Type':'application/json'}});
      assert.equal(deleted.status,200);
      assert.equal(requests.at(-2).url,`http://127.0.0.1:${port}/speech/jobs`);
      assert.equal(requests.at(-1).url,`http://127.0.0.1:${port}/speech/jobs/${id}`);
    }
    const invalid=await fetch(`${runtime.origin}/api/speech-motion/health?backend=http://foreign.example`);
    assert.equal(invalid.status,400);assert.equal(requests.length,6);
  } finally {await runtime.close();fs.rmSync(dataDir,{recursive:true,force:true});}
});

test('platform data paths are persistent and support explicit portable storage', () => {
  const home = path.resolve('/users/example');
  assert.equal(studioDataDirectory({ platform: 'darwin', env: {}, home }), path.join(home, 'Library', 'Application Support', 'YachiyoUnityDirector'));
  assert.equal(studioDataDirectory({ platform: 'linux', env: {}, home }), path.join(home, '.local', 'share', 'YachiyoUnityDirector'));
  assert.equal(studioDataDirectory({ platform: 'win32', env: { LOCALAPPDATA: home }, home }), path.join(home, 'YachiyoUnityDirector'));
  assert.equal(studioDataDirectory({ env: { YACHIYO_DATA_DIR: './portable' } }), path.resolve('portable'));
});

test('Windows launcher routes both generation and cancellation to its local service', async () => {
  const dataDir=fs.mkdtempSync(path.join(os.tmpdir(),'yachiyo-windows-route-')),requests=[];
  const previous={backend:process.env.YACHIYO_LOCAL_MOTION_BACKEND,url:process.env.YACHIYO_LOCAL_MOTION_URL};
  process.env.YACHIYO_LOCAL_MOTION_BACKEND='desktop';process.env.YACHIYO_LOCAL_MOTION_URL='http://127.0.0.1:8787/';
  const runtime=await startDesktopServer({resourceRoot:root,dataDir,port:0,services:{},fetchImpl:async(url)=>{requests.push(url);return new Response('{}');}});
  try {
    await post(runtime.origin,'/api/speech-motion/jobs?backend=desktop',{sampleRate:16000,pcm16:'AAA='});
    const removed=await fetch(`${runtime.origin}/api/speech-motion/jobs/${'a'.repeat(24)}?backend=desktop`,{method:'DELETE',headers:{'Content-Type':'application/json'}});
    assert.equal(removed.status,200);
    await fetch(`${runtime.origin}/api/speech-motion/health?backend=windows`);
    assert.deepEqual(requests,['http://127.0.0.1:8787/speech/jobs',`http://127.0.0.1:8787/speech/jobs/${'a'.repeat(24)}`,'http://127.0.0.1:18787/speech/health?after=0']);
  } finally {
    await runtime.close();fs.rmSync(dataDir,{recursive:true,force:true});
    for(const [key,value] of [['YACHIYO_LOCAL_MOTION_BACKEND',previous.backend],['YACHIYO_LOCAL_MOTION_URL',previous.url]]){if(value===undefined)delete process.env[key];else process.env[key]=value;}
  }
});

test('bundled SQLite preserves records across service restarts without Docker', { timeout: 40_000 }, async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'yachiyo-builtin-memory-'));
  let manager = makeManager(dataDir);
  try {
    const [base, second] = await Promise.all([manager.ensure('memory'), manager.ensure('memory')]);
    assert.equal(base, second, 'concurrent requests share one service');
    const health = await (await fetch(`${base}/healthz`)).json();
    assert.equal(health.sqliteReady, true);
    assert.equal(health.managedMilvus.autostart, false);
    assert.equal(health.databasePath, path.join(dataDir, 'MemoryData', 'yachiyo-memory.sqlite'));
    const settings = { provider: 'sqlite', milvusEnabled: true, milvusManaged: true, embeddingDimension: 64, gcEnabled: false };
    const initialized = await post(base, '/api/memory/init', settings);
    assert.equal(initialized.provider, 'sqlite');
    assert.equal(initialized.milvus.enabled, false, 'sqlite must override stale Milvus flags');
    assert.ok(initialized.indexed > 0, 'bundled persona seeds are imported');
    await post(base, '/api/memory/write', { ...settings, memory: { type: 'session', scope: 'long_term', title: 'Desktop persistent marker', text: 'Desktop persistent marker: yachiyo desktop remembers plum tea.', importance: 0.95, confidence: 1, tags: ['desktop-test'] } });
    await manager.close();
    manager = makeManager(dataDir);
    const restarted = await manager.ensure('memory');
    const recalled = await post(restarted, '/api/memory/search', { ...settings, query: { text: 'Desktop persistent marker plum tea', maxNotes: 8 } });
    assert.ok(recalled.notes.some(note => JSON.stringify(note).includes('plum tea')));
    const integrity = await post(restarted, '/api/memory/init', settings);
    assert.equal(integrity.organization.integrity, 'ok');
  } finally { await manager.close(); fs.rmSync(dataDir, { recursive: true, force: true }); }
});

test('gateway streams chat before completion, cancels upstream and rejects foreign origins', { timeout: 20_000 }, async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'yachiyo-gateway-'));
  let closedResolve;
  const closed = new Promise(resolve => { closedResolve = resolve; });
  const upstream = http.createServer(async (req, res) => {
    let body = '';
    for await (const part of req) body += part;
    const input = JSON.parse(body);
    assert.equal(input.messages[0].content, 'persona');
    if (!input.stream) { res.writeHead(200, { 'Content-Type': 'application/json' }); res.end('{"choices":[{"message":{"content":"reply"}}]}'); return; }
    res.writeHead(200, { 'Content-Type': 'text/event-stream' });
    res.write('data: {"choices":[{"delta":{"content":"VOICE: hi"}}]}\n\n');
    res.once('close', closedResolve);
  });
  await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
  const requests = [];
  const runtime = await startDesktopServer({ resourceRoot: root, dataDir, port: 0, services: { ensure: async () => { throw new Error('not used'); } }, fetchImpl: (...args) => { requests.push(args); return fetch(...args); } });
  try {
    const input = { apiUrl: `http://127.0.0.1:${upstream.address().port}/chat/completions`, model: 'test', systemPrompt: 'persona', message: 'hello' };
    const regular = await post(runtime.origin, '/api/chat', input);
    assert.equal(regular.data.reply, 'reply');
    const controller = new AbortController();
    const response = await fetch(`${runtime.origin}/api/chat/stream`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input), signal: controller.signal });
    const reader = response.body.getReader();
    const first = await reader.read();
    assert.match(new TextDecoder().decode(first.value), /VOICE: hi/);
    controller.abort();
    await Promise.race([closed, new Promise((_, reject) => setTimeout(() => reject(new Error('upstream did not cancel')), 2000))]);
    const denied = await fetch(`${runtime.origin}/api/chat`, { method: 'POST', headers: { Origin: 'https://foreign.example', 'Content-Type': 'application/json' }, body: '{}' });
    assert.equal(denied.status, 403);
    assert.equal(requests.length, 2);
    const page = await fetch(`${runtime.origin}/live2d-studio/`);
    assert.match(await page.text(), /<div id="app"/);
    const manifest = await fetch(`${runtime.origin}/models/tsukimi-yachiyo/vts-items.local.json`);
    assert.equal(manifest.status, 200);
    const managed = await fetch(`${runtime.origin}/api/memory/managed-milvus/start`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
    assert.equal(managed.status, 400);
  } finally { await runtime.close(); upstream.closeAllConnections(); await new Promise(resolve => upstream.close(resolve)); fs.rmSync(dataDir, { recursive: true, force: true }); }
});
