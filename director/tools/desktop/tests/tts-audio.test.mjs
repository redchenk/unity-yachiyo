import test from 'node:test';
import assert from 'node:assert/strict';
import { completeTtsWav, playableTtsBlob } from '../../../src/shared/tts-audio.mjs';
import { wav, vowelSamples } from '../lipsync-fixtures.mjs';
import { createServer } from 'vite';

function streamingWav() {
  const complete = wav(vowelSamples(0, 0.2));
  const streaming = Buffer.from(complete);
  streaming.writeUInt32LE(36, 4);
  streaming.writeUInt32LE(0, 40);
  return { complete, streaming };
}

test('GPT-SoVITS zero/unknown-length WAV retains PCM, sample rate and duration after completion', async () => {
  const { complete, streaming } = streamingWav();
  assert.deepEqual(Buffer.from(completeTtsWav(streaming)), complete);
  streaming.writeUInt32LE(0xffffffff, 4); streaming.writeUInt32LE(0xffffffff, 40);
  const playable = await playableTtsBlob(new Blob([streaming], { type: 'application/octet-stream' }));
  assert.equal(playable.type, 'audio/wav');
  assert.deepEqual(Buffer.from(await playable.arrayBuffer()), complete);
  assert.deepEqual(Buffer.from(completeTtsWav(complete)), complete);
});

test('WAV completion finds data after padded extra chunks and rejects truncated/empty samples', async () => {
  const { complete } = streamingWav();
  const withJunk = Buffer.concat([complete.subarray(0, 36), Buffer.from('JUNK\x01\x00\x00\x00x\x00', 'binary'), complete.subarray(36)]);
  withJunk.writeUInt32LE(0, 50);
  const repaired = Buffer.from(completeTtsWav(withJunk));
  assert.equal(repaired.readUInt32LE(50), complete.length - 44);
  assert.deepEqual(repaired.subarray(54), complete.subarray(44));
  assert.throws(() => completeTtsWav(complete.subarray(0, complete.length - 2)), /不完整/);
  assert.throws(() => completeTtsWav(complete.subarray(0, 44)), /不完整/);
  const { streaming } = streamingWav();
  assert.throws(() => completeTtsWav(streaming.subarray(0, streaming.length - 1)), /不完整/);
  await assert.rejects(playableTtsBlob(new Blob([])), /空音频/);
  await assert.rejects(playableTtsBlob(new Blob(['{"error":"failed"}'], { type: 'application/json' })), /不是音频/);
  const mp3 = new Uint8Array([73, 68, 51, 4, 0, 0]);
  const blob = await playableTtsBlob(new Blob([mp3], { type: 'audio/mpeg' }));
  assert.equal(blob.type, 'audio/mpeg'); assert.deepEqual(new Uint8Array(await blob.arrayBuffer()), mp3);
});

test('unified speech fetches the single-use URL once, completes WAV and aborts downloads on stop', async t => {
  const store = new Map(), audios = [], calls = [];
  const originalFetch = globalThis.fetch;
  const originals = Object.fromEntries(['window', 'localStorage', 'Audio', 'Image'].map(k => [k, globalThis[k]]));
  globalThis.localStorage = { getItem: k => store.get(k) || null, setItem: (k, v) => store.set(k, v) };
  globalThis.window = { yachiyoDesktop: { unified: true }, location: { protocol: 'http:', origin: 'http://127.0.0.1' },
    setTimeout, clearTimeout, requestAnimationFrame: () => 0, cancelAnimationFrame() {}, dispatchEvent() {}, addEventListener() {}, removeEventListener() {} };
  globalThis.Image = class { set src(_) { queueMicrotask(() => this.onerror?.()); } };
  globalThis.Audio = class {
    constructor(src) { this.src = src; this.dataset = {}; this.duration = 0.2; this.currentTime = 0; this.paused = true; audios.push(this); }
    async play() {
      const bytes = Buffer.from(await (await originalFetch(this.src)).arrayBuffer());
      assert.equal(bytes.readUInt32LE(40), bytes.length - 44);
      this.paused = false; this.onplaying?.();
      queueMicrotask(() => { this.ended = true; this.paused = true; this.onended?.(); });
    }
    pause() { this.paused = true; }
    removeAttribute() {} load() {}
  };
  const server = await createServer({ configFile: false, server: { middlewareMode: true, hmr: false }, appType: 'custom' });
  let player;
  t.after(async () => { player?.destroy(); await server.close(); globalThis.fetch = originalFetch; Object.assign(globalThis, originals); });
  let pending, upstreamSignal;
  const { streaming } = streamingWav();
  globalThis.fetch = async (url, options) => {
    calls.push(String(url));
    if (url === '/api/tts/prepare') return new Response(JSON.stringify({ url: '/api/tts/stream?token=fixture' }));
    if (url === '/api/tts') return new Response(streaming, { headers: { 'Content-Type': 'audio/wav' } });
    assert.equal(url, '/api/tts/stream?token=fixture');
    upstreamSignal = options.signal;
    if (!pending) return new Response(streaming, { headers: { 'Content-Type': 'audio/wav' } });
    return new Response(new ReadableStream({ start(controller) {
      controller.enqueue(streaming.subarray(0, 44));
      options.signal.addEventListener('abort', () => controller.error(new DOMException('Stopped', 'AbortError')), { once: true });
      pending();
    } }), { headers: { 'Content-Type': 'audio/wav' } });
  };
  const { createLive2DSpeechPlayer } = await server.ssrLoadModule('/src/frontend/services/room/live2dSpeech.js');
  store.set('roomTTSSettings', JSON.stringify({ enabled: true, provider: 'gpt-sovits', apiUrl: 'http://tts.invalid/tts' }));
  player = createLive2DSpeechPlayer();
  await player.enqueue('こんにちは。');
  assert.equal(calls.filter(x => x.includes('/stream?')).length, 1);
  assert.equal(audios.length, 1); assert.match(audios[0].src, /^blob:/);
  const downloadStarted = new Promise(resolve => { pending = resolve; });
  const cancelled = player.enqueue('テストです。').catch(e => e.name);
  await downloadStarted; player.stop();
  assert.equal(await cancelled, 'AbortError');
  assert.equal(upstreamSignal.aborted, true); assert.equal(audios.length, 1, 'cancelled download must not create a player');
  pending = null;
  store.set('roomTTSSettings', JSON.stringify({ enabled: true, provider: 'custom', useProxy: true }));
  await player.enqueue('もう一度。');
  assert.equal(audios.length, 2, 'the next line must recover after cancellation');
});
