const uuid = () => globalThis.crypto.randomUUID();
const clamp = n => Math.max(0, Math.min(1, Number(n) || 0));

// Semantic boundary: this runs before any Cubism parameter conversion.
export function createUnityAvatarDriver({ url, onStatus = () => {}, onReceipt = () => {}, WebSocketClass = WebSocket, events = window, getBodyMode = () => 'model' } = {}) {
  let socket, disposed = false, retry, sessionId = '', epoch = 0, seq = 0, ready = false;
  let activeAudio = null, lastMouthAt = 0, lastReceipt = null, lastEmotion = 'neutral';
  const audioIds = new WeakMap(), timers = new Set();
  const driver = { mode: 'unity', perform, cancel, audioStart, audioEnd, mouth, state, dispose, control: (action, value) => send('control', { action, value: typeof value === 'number' ? value : 0, text: typeof value === 'string' ? value : '' }),
    get ready() { return ready; }, get lastReceipt() { return lastReceipt; }, get context() { return { sessionId, epoch, utteranceId: activeAudio?.id || '' }; } };
  function send(kind, payload, ids = {}) {
    if (!ready || socket?.readyState !== 1) return null;
    const commandId = uuid();
    socket.send(JSON.stringify({ type: 'command', kind, payload, commandId, sessionId, epoch, seq: ++seq,
      turnId: ids.turnId || activeAudio?.turnId || `manual-${epoch}`,
      utteranceId: ids.utteranceId || activeAudio?.id || '' }));
    return commandId;
  }
  function clearLocal() { for (const timer of timers) clearTimeout(timer); timers.clear(); activeAudio = null; }
  function cancel() {
    clearLocal();
    if (!ready) return;
    send('cancel', {}); epoch++; // Block locally scheduled work immediately, before the round trip.
  }
  function perform(intent) {
    if (!intent || !ready) return null;
    const steps = Array.isArray(intent.sequence) && intent.sequence.length ? intent.sequence.slice(0, 12) : [intent];
    const context = { sessionId, epoch, utteranceId: activeAudio?.id || '' };
    let offset = 0;
    const commandIds = [];
    for (const step of steps) {
      offset += Math.max(0, Math.min(12000, Number(step.delayMs) || 0));
      const execute = () => {
        if (context.sessionId !== sessionId || context.epoch !== epoch || (context.utteranceId && context.utteranceId !== activeAudio?.id)) return;
        const stepEmotion = step.emotion || step.expression;
        if (stepEmotion) lastEmotion = stepEmotion;
        commandIds.push(send('perform', {
          emotion: stepEmotion || lastEmotion, intensity: clamp(step.intensity ?? .65),
          durationMs: Math.max(260, Math.min(15000, Number(step.durationMs) || 3500)),
          bodyMode: step.bodyMode || intent.bodyMode || getBodyMode(),
          priority: Math.max(0, Math.min(100, Math.round(Number(step.priority) || 0))),
          interruptPolicy: (typeof step.interruptPolicy === 'object' ? step.interruptPolicy?.mode : step.interruptPolicy) || 'queue',
          actions: step.behaviorActions || step.actions || (step.motion || step.bodyPose ? [{ type: step.motion || step.bodyPose }] : [])
        }, { turnId: step.turnId || intent.turnId, utteranceId: step.utteranceId || intent.utteranceId }));
      };
      if (offset) { const timer = setTimeout(() => { timers.delete(timer); execute(); }, offset); timers.add(timer); }
      else execute();
      offset += Math.max(260, Math.min(15000, Number(step.durationMs) || 3500));
    }
    return { ...intent, commandIds };
  }
  function state(detail = {}) { const emotion = detail.emotion || ''; if (emotion) lastEmotion = emotion; return send('state', { mode: detail.mode || 'idle', emotion, holdMs: Math.min(15000, Number(detail.holdMs) || 0) }); }
  function audioStart(audio, { source = 'speech', turnId = '', performance = null } = {}) {
    if (!ready) return;
    let token = audioIds.get(audio);
    if (activeAudio === token && token?.epoch === epoch && token.sessionId === sessionId) return;
    if (activeAudio) audioEnd(activeAudio.audio);
    token = { audio, source, id: uuid(), turnId: turnId || uuid(), epoch, sessionId };
    audioIds.set(audio, token); activeAudio = token;
    if (performance?.emotion) lastEmotion = performance.emotion;
    send('audio', { event: 'start', source, emotion: lastEmotion, performance, audioTimeMs: Math.round((audio.currentTime || 0) * 1000) }, { turnId: token.turnId, utteranceId: token.id });
  }
  function audioEnd(audio, event = 'end') {
    const token = audioIds.get(audio);
    if (!token || activeAudio !== token) return;
    send('audio', { event, source: token.source, audioTimeMs: Math.round((audio.currentTime || 0) * 1000) });
    activeAudio = null;
  }
  function mouth(detail = {}) {
    if (!activeAudio || detail.source !== activeAudio.source) return;
    const now = performance.now();
    // Coalesce hot analyser frames, but never delay a closing frame.
    if (now - lastMouthAt < 30 && Number(detail.value) > 0 && !detail.silence) return;
    lastMouthAt = now;
    const vowels = detail.vowels || {};
    send('mouth', { value: clamp(detail.value), silence: !!detail.silence,
      a: clamp(vowels[0] ?? vowels.a ?? vowels.A), i: clamp(vowels[1] ?? vowels.i ?? vowels.I), u: clamp(vowels[2] ?? vowels.u ?? vowels.U),
      e: clamp(vowels[3] ?? vowels.e ?? vowels.E), o: clamp(vowels[4] ?? vowels.o ?? vowels.O),
      audioTimeMs: Math.max(0, Math.round((activeAudio.audio.currentTime || 0) * 1000)) });
  }
  const mouthEvent = event => mouth(event.detail);
  const stateEvent = event => state(event.detail);
  events.addEventListener('tsukuyomi:live2d-mouth', mouthEvent);
  events.addEventListener('tsukuyomi:live2d-character-state', stateEvent);
  function connect() {
    if (disposed) return;
    socket = new WebSocketClass(url);
    socket.onmessage = ({ data }) => {
      let msg; try { msg = JSON.parse(data); } catch { return; }
      if (msg.type === 'ping') { socket.send(JSON.stringify({ type: 'ping' })); return; }
      if (msg.type === 'status') {
        if (sessionId !== msg.sessionId || epoch !== msg.epoch || !msg.ready) clearLocal();
        sessionId = msg.sessionId; epoch = msg.epoch; ready = !!msg.ready;
        onStatus(msg);
      }
      if (msg.type === 'receipt') { lastReceipt = msg; onReceipt(msg); events.dispatchEvent(new CustomEvent('yachiyo:avatar-receipt', { detail: msg })); }
      if (msg.type === 'interrupt') events.dispatchEvent(new Event('yachiyo:avatar-stop'));
    };
    socket.onerror = () => {};
    socket.onclose = () => { ready = false; clearLocal(); onStatus({ ready: false, error: '导演连接已断开，正在重连' }); if (!disposed) retry = setTimeout(connect, 1500); };
  }
  function dispose() { disposed = true; cancel(); clearTimeout(retry); socket?.close(); events.removeEventListener('tsukuyomi:live2d-mouth', mouthEvent); events.removeEventListener('tsukuyomi:live2d-character-state', stateEvent); }
  connect(); return driver;
}
