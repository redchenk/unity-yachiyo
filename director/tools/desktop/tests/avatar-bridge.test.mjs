import { test } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { once } from 'node:events';
import { WebSocket } from 'ws';
import { attachAvatarBridge } from '../avatar-bridge.mjs';
import { createUnityAvatarDriver } from '../../../src/frontend/services/room/unityAvatarDriver.mjs';

const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(check) { for (let n = 0; n < 200; n++) { if (check()) return; await delay(10); } throw new Error('Condition timeout'); }
async function fixture(t) {
  const server = http.createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const bridge = attachAvatarBridge(server);
  const url = `ws://127.0.0.1:${server.address().port}/avatar`;
  t.after(async () => { bridge.close(); await new Promise(resolve => server.close(resolve)); });
  const unity = new WebSocket(`${url}?role=unity`), messages = [];
  unity.on('message', data => messages.push(JSON.parse(data)));
  await once(unity, 'open');
  unity.send(JSON.stringify({ type: 'ready', capabilities: { actions: ['wave'], shapeCount: 29 } }));
  return { url, unity, messages };
}

test('real WebSocket driver retains semantics, gates audio, maps raw vowels and cancels queued work', async t => {
  const { url, unity, messages } = await fixture(t);
  let status; const receipts = [];
  const driver = createUnityAvatarDriver({ url: `${url}?role=director`, WebSocketClass: WebSocket, events: new EventTarget(), onStatus: s => status = s, onReceipt: r => receipts.push(r) });
  t.after(() => driver.dispose());
  await until(() => status?.ready);
  driver.mouth({ value: 1, vowels: [1, 0, 0, 0, 0], source: 'speech' });
  await delay(30); assert.equal(messages.filter(m => m.kind === 'mouth').length, 0, 'no audio, no mouth');
  const audio = { currentTime: .42 };
  driver.audioStart(audio, { turnId: 'turn-1' });
  driver.perform({ emotion: 'happy', behaviorActions: [{ type: 'wave', side: 'right' }], turnId: 'turn-1' });
  driver.mouth({ value: .8, vowels: [0, .7, .1, .1, .1], source: 'speech', silence: 0 });
  await until(() => messages.some(m => m.kind === 'mouth'));
  const mouth = messages.find(m => m.kind === 'mouth'), perform = messages.find(m => m.kind === 'perform');
  assert.equal(mouth.payload.i, .7); assert.equal(mouth.payload.audioTimeMs, 420);
  assert.equal(mouth.utteranceId, perform.utteranceId); assert.equal(perform.turnId, 'turn-1'); assert.equal(perform.payload.actions[0].type, 'wave');
  unity.send(JSON.stringify({ type: 'receipt', sessionId: perform.sessionId, epoch: perform.epoch, commandId: perform.commandId, state: 'started' }));
  await until(() => receipts.some(r => r.state === 'started'));
  driver.perform({ sequence: [{ delayMs: 150, emotion: 'sad' }] });
  const before = messages.filter(m => m.kind === 'perform').length;
  driver.cancel(); await delay(220);
  assert.equal(messages.filter(m => m.kind === 'perform').length, before);
  assert(receipts.some(r => r.state === 'cancelled'));
  const mouthCount = messages.filter(m => m.kind === 'mouth').length;
  driver.mouth({ value: 1, vowels: [1, 0, 0, 0, 0], source: 'speech' }); await delay(40);
  assert.equal(messages.filter(m => m.kind === 'mouth').length, mouthCount, 'late frames cannot reopen cancelled audio');
});

test('disconnect invalidates epoch, ignores stale receipts and never replays commands', async t => {
  const { url, unity, messages } = await fixture(t);let status;const receipts = [];
  const driver = createUnityAvatarDriver({ url: `${url}?role=director`, WebSocketClass: WebSocket, events: new EventTarget(), onStatus: s => status = s, onReceipt: r => receipts.push(r) });t.after(() => driver.dispose());
  await until(() => status?.ready);driver.perform({ emotion: 'happy' });await until(() => messages.some(m => m.kind === 'perform'));
  const old = messages.find(m => m.kind === 'perform');unity.close();await until(() => !status.ready);
  const fresh = new WebSocket(`${url}?role=unity`), freshMessages = [];fresh.on('message', d => freshMessages.push(JSON.parse(d)));await once(fresh, 'open');
  fresh.send(JSON.stringify({ type: 'ready', capabilities: { actions: ['wave'] } }));await until(() => status.ready);
  assert(status.epoch > old.epoch);await delay(50);assert(!freshMessages.some(m => m.kind === 'perform'));
  fresh.send(JSON.stringify({ type: 'receipt', ...old, type: 'receipt', state: 'completed' }));await delay(30);
  assert(!receipts.some(r => r.state === 'completed'));
});

test('foreign origins and duplicate avatar owners cannot access local gateway', async t => {
  const { url } = await fixture(t);
  for (const [options, code] of [[{ origin: 'https://foreign.example' }, 403], [{}, 409]]) {
    const socket = new WebSocket(`${url}?role=unity`, options);socket.on('error', () => {});
    const [, response] = await once(socket, 'unexpected-response');
    // ws emits (request, response).
    assert.equal(response.statusCode, code);socket.terminate();
  }
});
