import { WebSocketServer, WebSocket } from 'ws';
import { randomUUID } from 'node:crypto';

// Ephemeral, loopback-only transport. Never queues commands across a reconnect.
export function attachAvatarBridge(server, { allowedOrigin = () => `http://127.0.0.1:${server.address().port}`, observe = () => {} } = {}) {
  const wss = new WebSocketServer({ noServer: true, maxPayload: 64 * 1024 });
  let unity, director, sessionId = randomUUID(), epoch = 0, lastSeq = 0, capabilities = null;
  const pending = new Map();
  const send = (socket, value) => {
    if (socket?.readyState === WebSocket.OPEN && socket.bufferedAmount < 256 * 1024) socket.send(JSON.stringify(value));
  };
  const status = () => ({ type: 'status', sessionId, epoch, ready: !!unity && !!capabilities && capabilities.humanoid !== false && capabilities.audioVisemes !== false, capabilities });
  const receipt = (command, state, reason = '') => send(director, { ...command, type: 'receipt', state, reason });
  function invalidate(reason) {
    epoch++;
    for (const command of pending.values()) receipt(command, 'cancelled', reason);
    pending.clear();
    send(unity, { type: 'command', kind: 'cancel', sessionId, epoch, reason });
    send(director, status());
  }
  function upgrade(req, socket, head) {
    let url;
    try { url = new URL(req.url, allowedOrigin()); } catch { socket.destroy(); return; }
    if (url.pathname !== '/avatar') return;
    const origin = allowedOrigin();
    if (req.headers.host !== new URL(origin).host || (req.headers.origin && req.headers.origin !== origin)
      || !['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(req.socket.remoteAddress)) {
      socket.end('HTTP/1.1 403 Forbidden\r\n\r\n'); return;
    }
    const role = url.searchParams.get('role');
    if (!['unity', 'director'].includes(role) || (role === 'unity' && unity) || (role === 'director' && director)) {
      socket.end('HTTP/1.1 409 Conflict\r\n\r\n'); return;
    }
    wss.handleUpgrade(req, socket, head, ws => wss.emit('connection', ws, role));
  }
  server.on('upgrade', upgrade);
  wss.on('connection', (ws, role) => {
    let lastMessage = Date.now(), frames = 0, windowAt = Date.now();
    if (role === 'unity') { unity = ws; capabilities = null; invalidate('unity-connected'); }
    else { director = ws; sessionId = randomUUID(); lastSeq = 0; invalidate('director-connected'); }
    send(ws, status());
    ws.on('message', data => {
      lastMessage = Date.now();
      if (lastMessage - windowAt > 1000) { frames = 0; windowAt = lastMessage; }
      if (++frames > 160) { ws.close(1008, 'Rate limit'); return; }
      let msg;
      try { msg = JSON.parse(data.toString()); } catch { ws.close(1007, 'JSON required'); return; }
      if(msg.kind === 'audio') observe({role, msg, expectedEpoch: epoch, expectedSession: sessionId, lastSeq});
      if (msg.type === 'ping') { send(ws, { type: 'pong' }); return; }
      if (role === 'unity') {
        if (msg.type === 'interrupt') { invalidate('unity-local-stop'); send(director, { type: 'interrupt' }); return; }
        if (msg.type === 'ready') {
          capabilities = msg.capabilities && typeof msg.capabilities === 'object' ? msg.capabilities : null;
          send(director, status()); return;
        }
        if (msg.type !== 'receipt' || msg.sessionId !== sessionId || msg.epoch !== epoch) return;
        const command = pending.get(msg.commandId);
        if (!command || !['accepted', 'started', 'completed', 'cancelled', 'unsupported', 'failed'].includes(msg.state)) return;
        receipt(command, msg.state, String(msg.reason || '').slice(0, 512));
        if (['completed', 'cancelled', 'unsupported', 'failed'].includes(msg.state)) pending.delete(msg.commandId);
        return;
      }
      if (msg.type !== 'command' || msg.sessionId !== sessionId || msg.epoch !== epoch || !Number.isSafeInteger(msg.seq) || msg.seq <= lastSeq) return;
      lastSeq = msg.seq;
      if (!['perform', 'state', 'audio', 'mouth', 'motion', 'cancel', 'control'].includes(msg.kind)) return;
      if (msg.kind === 'cancel') { invalidate('director-cancel'); return; }
      const command = { type: 'command', kind: msg.kind, sessionId, epoch, seq: msg.seq,
        commandId: String(msg.commandId || '').slice(0, 96), turnId: String(msg.turnId || '').slice(0, 96),
        utteranceId: String(msg.utteranceId || '').slice(0, 96), payload: msg.payload || {} };
      if (!unity || !capabilities) { if (msg.kind === 'perform') receipt(command, 'failed', 'Unity is not ready'); return; }
      if (msg.kind === 'perform') {
        if (!command.commandId || pending.has(command.commandId)) return;
        if (pending.size >= 64) { receipt(command, 'failed', 'Performance queue full'); return; }
        pending.set(command.commandId, { ...command, expiresAt: Date.now() + 90000 });
      }
      // Close slow clients instead of silently losing cancellation/audio boundaries.
      if (unity.bufferedAmount >= 256 * 1024) { unity.close(1013, 'Receiver too slow'); return; }
      send(unity, command);
    });
    ws.on('error', () => {});
    ws.on('close', () => {
      if (unity === ws) { unity = null; capabilities = null; invalidate('unity-disconnected'); }
      if (director === ws) { invalidate('director-disconnected'); director = null; }
    });
    const heartbeat = setInterval(() => {
      if (Date.now() - lastMessage > 20000) ws.terminate();
      else send(ws, { type: 'ping' });
      for (const [id, cmd] of pending) if (Date.now() > cmd.expiresAt) { receipt(cmd, 'failed', 'Execution timeout'); pending.delete(id); }
    }, 5000);
    heartbeat.unref(); ws.on('close', () => clearInterval(heartbeat));
  });
  return { get backend() { return capabilities?.selectedBackend || 'windows'; }, close() { server.off('upgrade', upgrade); for (const client of wss.clients) client.terminate(); wss.close(); } };
}
