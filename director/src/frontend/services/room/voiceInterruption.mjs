// Conservative sustained voice gate after browser echo cancellation. This is
// opt-in: RMS cannot distinguish a loudspeaker echo from a person's voice.
export function createVoiceInterruptionGate({ threshold = .035, sustainMs = 260 } = {}) {
  let since = null, last = null;
  return {
    reset() { since = last = null; },
    sample(level, time, vadThreshold = .01) {
      if (!Number.isFinite(level) || !Number.isFinite(time)) return false;
      if (last !== null && (time < last || time - last > 350)) since = null;
      last = time;
      if (level < Math.max(threshold, vadThreshold * 2)) { since = null; return false; }
      if (since === null) since = time;
      if (time - since < sustainMs) return false;
      since = null; return true;
    }
  };
}
