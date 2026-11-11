import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'vite';

test('continuous recorder interrupts only when opted in, retains onset audio and drops stale transcripts', async t => {
  const keys = ['window', 'navigator', 'localStorage', 'FileReader', 'fetch', 'performance'];
  const originals = Object.fromEntries(keys.map(k => [k, Object.getOwnPropertyDescriptor(globalThis, k)]));
  const states = [], results = [], recognitions = []; let processor, time = 1000, interrupted = 0, permissions = 0, stopped = 0;
  const set = (k, value) => Object.defineProperty(globalThis, k, { value, configurable: true, writable: true });
  const store = new Map([['roomASRSettings', JSON.stringify({ enabled: true, vadThreshold: .012, continuousSilenceMs: 400, minSpeechMs: 200 })]]);
  set('localStorage', { getItem: k => store.get(k) || null, setItem: (k, v) => store.set(k, v) });
  set('window', { location: { protocol: 'http:' }, setTimeout, clearTimeout, dispatchEvent() {}, AudioContext: class {
    sampleRate = 16000; destination = {};
    createMediaStreamSource() { return { connect() {}, disconnect() {} }; }
    createScriptProcessor() { processor = { connect() {}, disconnect() {} }; return processor; }
    close() { return Promise.resolve(); }
  } });
  set('navigator', { mediaDevices: { async getUserMedia(options) { permissions++; assert.equal(options.audio.echoCancellation, true); return { getTracks: () => [{ stop() { stopped++; } }] }; } } });
  set('FileReader', class { async readAsDataURL(blob) { this.result = 'data:audio/wav;base64,' + Buffer.from(await blob.arrayBuffer()).toString('base64'); this.onload(); } });
  set('fetch', async (_url, options) => new Promise(resolve => recognitions.push({ body: JSON.parse(options.body), resolve: text => resolve(new Response(JSON.stringify({ success: true, data: { text } }))) })));
  const server = await createServer({ configFile: false, server: { middlewareMode: true, hmr: false }, appType: 'custom' });
  let recorder;
  t.after(async () => { recorder?.destroy(); await server.close(); for (const k of keys) if (originals[k]) Object.defineProperty(globalThis, k, originals[k]); else delete globalThis[k]; });
  const { createLive2DAsrRecorder } = await server.ssrLoadModule('/src/frontend/services/room/live2dAsr.js');
  set('performance', { now: () => time });
  recorder = createLive2DAsrRecorder({ onState: s => states.push(s), onResult: r => results.push(r), onSpeechStart: () => { interrupted++; recorder.setSuppressed(false); } });
  assert.equal(permissions, 0, 'constructing a recorder must not start microphone capture');
  await recorder.start({ continuous: true }); recorder.setSuppressed(true);
  const feed = level => { time += 100; const pcm = new Float32Array(1600).fill(level); processor.onaudioprocess({ inputBuffer: { getChannelData: () => pcm } }); };
  for (let i = 0; i < 6; i++) feed(.15);
  assert.equal(interrupted, 0); recorder.setBargeInEnabled(true);
  for (let i = 0; i < 4; i++) feed(.15);
  assert.equal(interrupted, 1); assert.equal(states.at(-1).suppressed, false);
  for (let i = 0; i < 3; i++) feed(.15);
  for (let i = 0; i < 12; i++) feed(0);
  await new Promise(r => setTimeout(r, 20)); assert.equal(recognitions.length, 1);
  const wav = Buffer.from(recognitions[0].body.audioBase64, 'base64'); assert(wav.length > 16000, 'voice onset was lost during suppression reset');
  recorder.invalidatePending(); recognitions[0].resolve('stale first sentence');
  await new Promise(r => setTimeout(r, 20)); assert.equal(results.length, 0);
  for (let i = 0; i < 7; i++) feed(.15);
  for (let i = 0; i < 12; i++) feed(0);
  await new Promise(r => setTimeout(r, 20)); assert.equal(recognitions.length, 2);
  recognitions[1].resolve('new sentence'); await new Promise(r => setTimeout(r, 20));
  assert.equal(results[0].text, 'new sentence'); recorder.destroy(); assert.equal(stopped, 1);
});
