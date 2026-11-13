import test from 'node:test';
import assert from 'node:assert/strict';
import { createSpeechMotionBuffer } from '../../../src/frontend/services/room/speechMotionBuffer.mjs';

const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; };
function fixture(overrides = {}) {
  const calls = [], events = [];
  const motion = { starts: 0, stops: 0, start() { this.starts++; }, stop() { this.stops++; } };
  const buffer = createSpeechMotionBuffer({
    decode: async () => ({ duration: 6 }), backend: () => 'desktop', enabled: () => true,
    report: event => events.push(event), prepare: async (pcm, options) => { calls.push(options); return motion; },
    ...overrides
  });
  return { buffer, calls, events, motion };
}

test('queued motion is prepared once and does not take the playing audio owner', async () => {
  const f = fixture(), audio = {};
  const prefetched = f.buffer.get(audio, { prefetch: true });
  const playing = f.buffer.get(audio);
  assert.equal(prefetched, playing);
  assert.equal(await playing, f.motion);
  assert.equal(f.calls.length, 1);
  assert.equal(f.calls[0].backend, 'desktop');
  assert.equal(f.events[0].prefetch, true);
  assert.equal(f.motion.starts, 0);
  f.buffer.release(audio); assert.equal(f.motion.stops, 1);
});

test('removing a queued utterance during decoding never submits its GPU job', async () => {
  const decoded = deferred(), f = fixture({ decode: () => decoded.promise }), audio = {};
  const pending = f.buffer.get(audio);
  f.buffer.release(audio); decoded.resolve({ duration: 6 });
  await assert.rejects(pending, { name: 'AbortError' });
  assert.equal(f.calls.length, 0);
});

test('cancelled in-flight prefetch disposes late results and permits re-preparation', async () => {
  const generated = deferred(); let options;
  const f = fixture({ prepare: (_, o) => { options = o; return generated.promise; } }), audio = {};
  const pending = f.buffer.get(audio);
  await Promise.resolve(); f.buffer.release(audio);
  assert(options.cancelled());
  generated.resolve(f.motion);
  await assert.rejects(pending, { name: 'AbortError' });
  assert.equal(f.motion.stops, 1);
  assert.equal(await f.buffer.get(audio), f.motion);
});

test('backend changes discard the old host result, and expiry cancels prefetch', async () => {
  let host = 'windows', expired = false;
  const f = fixture({ backend: () => host }), audio = {};
  await f.buffer.get(audio, { cancelled: () => expired });
  expired = true; assert(f.calls[0].cancelled());
  host = 'desktop';
  await f.buffer.get(audio);
  assert.equal(f.motion.stops, 1);
  assert.deepEqual(f.calls.map(c => c.backend), ['windows', 'desktop']);
});

test('disabled neural motion never decodes or fabricates a fallback', async () => {
  const f = fixture({ decode: () => assert.fail('Unexpected decode') });
  assert.equal(await f.buffer.get({}, { neuralMotion: false }), null);
  assert.equal(f.calls.length, 0);
});
