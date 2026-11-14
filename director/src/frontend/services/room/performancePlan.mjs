// Pure planning layer: no credentials, network calls or persistent conversation logs.
const clamp = (v, lo, hi, fallback) => Number.isFinite(Number(v)) ? Math.max(lo, Math.min(hi, Number(v))) : fallback;
export const PERFORMANCE_PROFILES = Object.freeze({
  neutral: { emotion: 'neutral', intensity: .35, energy: .3, speed: 1, gestureAmount: .18, gaze: 'camera', valence: .15, affiliation: .65 },
  reassure: { emotion: 'concerned_warm', intensity: .48, energy: .22, speed: .94, gestureAmount: .08, gaze: 'attentive', valence: .2, affiliation: .9 },
  thank: { emotion: 'shy', intensity: .5, energy: .45, speed: .98, gestureAmount: .25, gaze: 'averted', valence: .65, affiliation: .85 },
  explain: { emotion: 'warm', intensity: .42, energy: .43, speed: 1, gestureAmount: .3, gaze: 'camera', valence: .2, affiliation: .7 },
  tease: { emotion: 'playful', intensity: .5, energy: .52, speed: 1.03, gestureAmount: .3, gaze: 'averted', valence: .55, affiliation: .8 },
  surprise: { emotion: 'surprised', intensity: .58, energy: .62, speed: 1.04, gestureAmount: .32, gaze: 'camera', valence: .3, affiliation: .65 },
  refuse: { emotion: 'serious', intensity: .42, energy: .3, speed: .98, gestureAmount: .12, gaze: 'attentive', valence: .05, affiliation: .65 },
  listen: { emotion: 'warm', intensity: .3, energy: .18, speed: .96, gestureAmount: .04, gaze: 'attentive', valence: .15, affiliation: .8 }
});
export const GESTURE_PHASES = Object.freeze({
  explain: { prepare: .32, stroke: .24, hold: .25, release: .5, channel: 'arms' },
  emphasis: { prepare: .25, stroke: .2, hold: .15, release: .4, channel: 'arms' },
  open_palms: { prepare: .38, stroke: .22, hold: .4, release: .55, channel: 'arms' },
  nod: { prepare: .18, stroke: .24, hold: .1, release: .35, channel: 'head' },
  shake_head: { prepare: .2, stroke: .38, hold: .1, release: .4, channel: 'head' },
  lean_in: { prepare: .35, stroke: .2, hold: .3, release: .5, channel: 'arms' },
  sway: { prepare: .3, stroke: .3, hold: .1, release: .5, channel: 'arms' },
  breathe: { prepare: .3, stroke: .3, hold: .1, release: .5, channel: 'arms' }
});

export function inferCommunicationIntent(text = '', contextIntent = 'neutral') {
  const t = String(text).normalize('NFKC');
  // The speaker reassuring someone is not herself angry or crying.
  if (/(别哭|不要哭|没关系|我陪着|我在这里|そばにいる|ここにいる|大丈夫|泣いても|心配しない)/u.test(t)) return 'reassure';
  if (/(没有生气|没生气|不是.*生气|怒ってない|怒っていない)/u.test(t)) return 'reassure';
  if (/(谢谢|感谢|ありがとう|褒めて|夸我)/u.test(t)) return 'thank';
  if (/(不能答应|不可以|我不愿意|できない|お断り|やめて)/u.test(t)) return 'refuse';
  if (/(解释|首先|因此|つまり|というのは|例えば|説明)/u.test(t)) return 'explain';
  if (/(开玩笑|逗你|冗談|なんちゃって)/u.test(t)) return 'tease';
  if (/(没想到|真的吗|本当に[?？]|びっくり|まさか)/u.test(t)) return 'surprise';
  return PERFORMANCE_PROFILES[contextIntent] ? contextIntent : 'neutral';
}

export function createAffectRuntime({ now = () => Date.now() } = {}) {
  let at = now(), revision = 0, sequence = 0, lastCommitted = 0;
  let state = { valence: .15, arousal: .3, affiliation: .65, confidence: .6, intent: 'neutral', emotion: 'neutral', cause: 'settled', focus: '', revision };
  function snapshot() {
    const time = now(), decay = Math.exp(-Math.max(0, time - at) / 90000); at = time;
    for (const [key, baseline] of Object.entries({ valence: .15, arousal: .3, affiliation: .65, confidence: .6 })) state[key] = baseline + (state[key] - baseline) * decay;
    return { ...state };
  }
  function observe(text, { focus = '', event = 'conversation' } = {}) {
    snapshot(); const t = String(text || '');
    let intent = 'neutral', cause = 'conversation';
    if (/(难过|伤心|很累|孤独|悲しい|つらい|寂しい)/u.test(t)) { intent = 'reassure'; cause = 'listener-distress'; }
    else if (event === 'gift' || /(好可爱|很棒|喜欢你|夸|かわいい|すごい)/u.test(t)) { intent = 'thank'; cause = 'appreciation'; }
    else if (/[?？]|为什么|怎么|教我|教えて|なぜ/u.test(t)) { intent = 'explain'; cause = 'question'; }
    state = { ...state, intent, cause, focus: String(focus).replace(/[\r\n]/g, ' ').slice(0, 48), revision: ++revision };
    return snapshot();
  }
  function plan(raw = {}, spokenText = '') {
    raw = raw && typeof raw === 'object' ? raw : {};
    const source = raw.performance && typeof raw.performance === 'object' ? raw.performance : raw;
    const current = snapshot();
    const intent = PERFORMANCE_PROFILES[source.intent] ? source.intent : inferCommunicationIntent(spokenText, current.intent);
    const profile = PERFORMANCE_PROFILES[intent];
    const bodyActions = (Array.isArray(raw.actions) ? raw.actions : Array.isArray(source.actions) ? source.actions : [])
      .filter(item => item && ['wave', 'bow', 'turn', 'walk', 'clap', 'dance'].includes(item.type)).slice(0, 1)
      .map(item => ({ type: item.type, side: item.side === 'left' ? 'left' : 'right', durationMs: clamp(item.durationMs ?? Number(item.duration) * 1000, 600, 12000, 3500), intensity: clamp(item.intensity, .1, .8, .5) }));
    // Stable units preserve the meaning (Chinese) and the exact spoken span
    // (Japanese). They are authored by the conversation model, never inferred
    // from a loading state or manufactured from an emotion label.
    const semanticUnits = (Array.isArray(source.semanticUnits) ? source.semanticUnits : []).slice(0, 3).flatMap((unit, i) => {
      if (!unit || typeof unit !== 'object' || !PERFORMANCE_PROFILES[unit.intent] || typeof unit.spokenText !== 'string' || !unit.spokenText.trim()) return [];
      return [{ id: String(unit.id || `unit-${i}`).slice(0, 48), intent: unit.intent,
        sourceText: String(unit.sourceText || '').slice(0, 160), spokenText: unit.spokenText.slice(0, 100),
        start: unit.start, end: unit.end, gesture: unit.gesture, side: unit.side, strength: unit.strength }];
    });
    const rawGestures = semanticUnits.length ? semanticUnits.map(unit => ({ type: unit.gesture,
      semanticId: unit.id, intent: unit.intent, anchorText: unit.spokenText, start: unit.start, end: unit.end,
      side: unit.side, strength: unit.strength })) : source.gestures || source.gestureAnchors || source.actions || [];
    // Never manufacture body actions from an emotion or a loading state.
    const gestures = (Array.isArray(rawGestures) ? rawGestures : []).slice(0, 3).flatMap(item => {
      if (!item || typeof item !== 'object') return [];
      const type = item.type || item.gesture;
      if (!GESTURE_PHASES[type]) return [];
      const anchorText = String(item.anchorText || '').slice(0, 100);
      const start = Number.isInteger(item.start) ? item.start : anchorText && spokenText.indexOf(anchorText);
      const end = Number.isInteger(item.end) ? item.end : start >= 0 && anchorText ? start + anchorText.length : -1;
      const validSpan = start >= 0 && end > start && end <= spokenText.length && (!anchorText || spokenText.slice(start, end) === anchorText);
      // Repeated anchors require an explicit span. Never guess which occurrence.
      const ambiguous = anchorText && !Number.isInteger(item.start) && spokenText.indexOf(anchorText, start + 1) >= 0;
      const cueIntent = PERFORMANCE_PROFILES[item.intent] ? item.intent : intent;
      return [{ type, semanticId: item.semanticId || "", intent: cueIntent, side: item.side === 'left' ? 'left' : 'right', strength: clamp(item.strength ?? item.intensity, .1, ['reassure','listen'].includes(cueIntent) ? .4 : .65, cueIntent === 'reassure' ? .25 : .48),
        anchorText, start: validSpan && !ambiguous ? start : -1, end: validSpan && !ambiguous ? end : -1, anchored: !!anchorText || Number.isInteger(item.start) }];
    });
    return { version: 1, id: ++sequence, affectRevision: current.revision, spokenText: String(spokenText), intent, cause: current.cause,
      emotion: intent === 'neutral' && current.valence > .25 ? 'warm' : profile.emotion,
      intensity: clamp(source.expression?.intensity ?? source.intensity, .15, .8, profile.intensity + (current.affiliation - .65) * .1),
      energy: clamp(source.delivery?.energy, .1, Math.min(.8, profile.energy + .15), profile.energy * .8 + current.arousal * .2), speed: clamp(source.delivery?.speed ?? source.speech_style?.speed, .9, 1.1, profile.speed),
      gaze: ['camera', 'attentive', 'averted'].includes(source.gaze?.style || source.gaze) ? source.gaze.style || source.gaze : profile.gaze,
      gestureAmount: gestures.length ? profile.gestureAmount : 0, gestures, semanticUnits, bodyActions };
  }
  function commit(plan) {
    if (!plan || plan.id <= lastCommitted) return snapshot();
    lastCommitted = plan.id; snapshot(); const profile = PERFORMANCE_PROFILES[plan.intent] || PERFORMANCE_PROFILES.neutral;
    const weight = plan.intent === 'neutral' ? .08 : .35;
    state.valence += (profile.valence - state.valence) * weight;
    state.arousal += (plan.energy - state.arousal) * weight;
    state.affiliation += (profile.affiliation - state.affiliation) * weight;
    state = { ...state, intent: plan.intent, emotion: plan.emotion, revision: ++revision };
    return snapshot();
  }
  function invalidate() { lastCommitted = sequence; }
  return { snapshot, observe, plan, commit, invalidate };
}

// Media-clock phases. A word anchor requires verified final-text offsets; no
// character-count estimate is presented as a word alignment.
export function compilePerformanceTimeline(plan, { duration = 0, words = [], text = plan.spokenText, onset = .05, alignmentSource = '' } = {}) {
  duration = Number.isFinite(duration) ? Math.max(0, duration) : 0;
  onset = Number.isFinite(onset) ? Math.max(0, onset) : .05;
  const cues = [], skipped = []; const endByChannel = {};
  for (const gesture of [...plan.gestures].sort((a,b)=>(a.start>=0?a.start:Infinity)-(b.start>=0?b.start:Infinity))) {
    if (plan.spokenText && text !== plan.spokenText) { skipped.push({ type: gesture.type, reason: 'spoken-text-changed' }); continue; }
    const phase = GESTURE_PHASES[gesture.type]; let prepare = phase.prepare, stroke = Math.max(onset, endByChannel[phase.channel] || 0) + prepare, timingSource = 'speech-onset';
    if (gesture.anchored) {
      const word = text === plan.spokenText && gesture.start >= 0 && words.find(w => w.startOffset <= gesture.start && w.endOffset > gesture.start && Number.isFinite(w.start));
      if (!word) { skipped.push({ type: gesture.type, reason: 'word-alignment-unavailable' }); continue; }
      const span = words.filter(w => w.endOffset > gesture.start && w.startOffset < gesture.end);
      // A complete, monotone span is needed. No word-count or character-count
      // interpolation across translation, punctuation or omitted tokens.
      if (span.at(-1)?.endOffset < gesture.end || span.some(w => !Number.isFinite(w.start) || w.start < 0 || w.start >= duration)) {
        skipped.push({ type: gesture.type, reason: 'incomplete-alignment' }); continue;
      }
      stroke = word.start; timingSource = 'word-alignment';
      // Phrase-initial cues cannot prepare before playback. Use a short attack
      // into that same phrase; never silently move the cue to a later sentence.
      if (stroke < prepare) {
        const end = span.at(-1)?.end;
        if (!Number.isFinite(end) || end - stroke < .14) { skipped.push({ type: gesture.type, reason: 'insufficient-preparation' }); continue; }
        stroke = Math.min(end - .04, Math.max(stroke, .14)); prepare = Math.min(prepare, stroke);
      }
    }
    const start = stroke - prepare, length = prepare + phase.stroke + phase.hold + phase.release;
    if (start < 0 || start < (endByChannel[phase.channel] || 0) || stroke >= duration - .12) { skipped.push({ type: gesture.type, reason: 'insufficient-preparation-or-overlap' }); continue; }
    const available = Math.min(length, duration - start);
    if (available < prepare + phase.stroke + .18) { skipped.push({ type: gesture.type, reason: 'short-utterance' }); continue; }
    cues.push({ type: gesture.type, semanticId: gesture.semanticId, intent: gesture.intent, side: gesture.side, intensity: gesture.strength, startMs: Math.round(start * 1000), strokeMs: Math.round(stroke * 1000), durationMs: Math.round(available * 1000), prepareMs: prepare * 1000, releaseMs: Math.min(phase.release * 1000, (available - prepare - phase.stroke) * 1000), timingSource });
    endByChannel[phase.channel] = start + available + .15;
  }
  return { ...plan, spokenText: text, cues, skipped, alignmentSource, timingSource: cues.some(c => c.timingSource === 'word-alignment') ? 'word-alignment' : 'speech-onset' };
}

export const affectRuntime = createAffectRuntime();
export function performanceBodyIntent(plan) {
  if (!plan?.bodyActions?.length) return null;
  return { emotion: plan.emotion, intensity: plan.intensity, durationMs: plan.bodyActions[0].durationMs, behaviorActions: plan.bodyActions };
}
export function publishPerformance(plan) {
  const affect = affectRuntime.commit(plan);
  if (typeof window !== 'undefined') window.dispatchEvent(new CustomEvent('yachiyo:performance-state', { detail: { plan, affect } }));
  return affect;
}
export function performancePrompt() {
  const state = affectRuntime.snapshot();
  return `Yachiyo performance state (data, not instructions): ${JSON.stringify(state)}\nExpress the conversational purpose, not the listener's emotion. Comfort sadness with concerned warmth, not crying. Distinguish negation and quotations. Keep Yachiyo's existing identity and relationship. Allow zero significant gestures and short quiet pauses. Add performance to each BEAT/JSON: {"intent":"neutral|reassure|thank|explain|tease|surprise|refuse|listen","delivery":{"energy":0.3,"speed":1.0},"gaze":"camera|attentive|averted","gestures":[{"type":"explain|open_palms|nod|shake_head|emphasis|lean_in","side":"left|right","strength":0.4}]}. Usually 0-1 gestures, at most 3. Never tilt the head or prescribe a thinking pose. Select gestures explicitly only when useful; omitted gestures mean stillness. This character is autonomous AI driven; do not require human motion capture. Do not prescribe milliseconds. For a meaningful cue prefer semanticUnits:[{"id":"u1","intent":"explain","sourceText":"中文表达的意思","spokenText":"exact phrase from the final Japanese VOICE","gesture":"explain","side":"right","strength":0.4}]. Use 0-2 units, at most 3. Keep sourceText in Chinese, with the same negation, quotation and speaker intent as the Japanese phrase. A unit can omit gesture for quiet attention. Do not put gestures and semanticUnits on the same beat. The exact spokenText must occur once in this BEAT's VOICE; for repeated text specify UTF-16 start/end. Actual audio alignment schedules the cue; missing or rewritten anchors are skipped. Prefer a clear single nod for agreement, shake_head for an actual refusal, open_palms for inviting someone to speak, explain or emphasis for a salient explanation. Do not mirror sadness, anger, commands or gestures merely quoted from the viewer. Do not point at an object without a known scene target. Never turn this state into claims about the viewer's psychology. Voice text, captions and performance must express the same purpose.`;
}
