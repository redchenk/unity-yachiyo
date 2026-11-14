import { prepareSpeechMotion } from './speechMotion.mjs';

// A queued utterance may prepare its first neural window while another audio
// element is playing. Preparation never takes the avatar or audio-clock owner.
export function createSpeechMotionBuffer({ decode, backend, enabled, report = () => {}, prepare = prepareSpeechMotion }) {
  const entries = new WeakMap();
  const abort = () => new DOMException('Speech motion cancelled', 'AbortError');
  function release(audio) {
    const entry = entries.get(audio);
    if (!entry) return;
    entry.cancelled = true;
    entry.motion?.stop();
    entries.delete(audio);
  }
  function get(audio, { cancelled = () => false, prefetch = false, neuralMotion = true, spokenText = '' } = {}) {
    if (!enabled() || !neuralMotion) { release(audio); return Promise.resolve(null); }
    const host = backend();
    let entry = entries.get(audio);
    if (entry && entry.backend === host && entry.spokenText === spokenText) return entry.promise;
    release(audio);
    entry = { backend: host, spokenText, cancelled: false, motion: null };
    entries.set(audio, entry);
    const stale = () => entry.cancelled || cancelled() || backend() !== host || !enabled();
    entry.promise = (async () => {
      const pcm = await decode(audio);
      if (stale()) throw abort();
      if (!pcm) return null;
      report({ status: 'loading', model: 'StreamTalk', prefetch });
      const motion = await prepare(pcm, {
        backend: host, spokenText, cancelled: stale,
        report: detail => report({ ...detail, prefetch })
      });
      if (stale()) { motion.stop(); throw abort(); }
      entry.motion = motion;
      return motion;
    })();
    return entry.promise;
  }
  return { get, release };
}
