import test from 'node:test';
import assert from 'node:assert/strict';
import { createAffectRuntime, compilePerformanceTimeline, inferCommunicationIntent, performanceBodyIntent } from '../../../src/frontend/services/room/performancePlan.mjs';
import { createVoiceInterruptionGate } from '../../../src/frontend/services/room/voiceInterruption.mjs';
import { buildTtsRequest } from '../tts.mjs';

test('six conversational purposes produce distinct face, voice, gaze and body profiles', () => {
  const affect = createAffectRuntime();
  const cases = [
    ['ありがとう。うれしいよ。', 'thank', 'shy', 'nod'],
    ['泣いてもいいよ。そばにいるから。', 'reassure', 'concerned_warm', 'open_palms'],
    ['例えば、こういうことだよ。', 'explain', 'warm', 'explain'],
    ['なんちゃって、冗談だよ。', 'tease', 'playful', 'head_tilt'],
    ['まさか、本当に？', 'surprise', 'surprised', 'open_palms'],
    ['それはお断りするね。', 'refuse', 'serious', 'shake_head']
  ];
  for (const [text, intent, emotion, gesture] of cases) {
    const p = affect.plan({}, text), timeline = compilePerformanceTimeline(p, { duration: 4 });
    assert.equal(p.intent, intent); assert.equal(p.emotion, emotion); assert.equal(timeline.cues[0].type, gesture);
    assert(timeline.cues[0].durationMs < 1800); assert(p.speed >= .9 && p.speed <= 1.1);
  }
  assert.equal(inferCommunicationIntent('我没有生气，别哭。'), 'reassure');
  assert.equal(inferCommunicationIntent('怒ってないよ。'), 'reassure');
});

test('explicit quiet listening has no gestures; calming is not crying or arm flapping', () => {
  const affect = createAffectRuntime(); affect.observe('今天我很难过');
  const plan = affect.plan({ performance: { intent: 'reassure', gestures: [] } }, '聞いているよ。');
  assert.equal(plan.emotion, 'concerned_warm'); assert.equal(plan.gestureAmount, 0);
  assert.deepEqual(compilePerformanceTimeline(plan, { duration: 12 }).cues, []);
  assert(plan.energy < .3); assert(plan.speed < 1);
  assert.doesNotThrow(() => affect.plan({ gestures: [null, 2, { type: 'fly' }] }));
});

test('explicit model actions retain their existing route beside conversational gestures', () => {
  const affect = createAffectRuntime();
  const plan = affect.plan({ performance: { intent: 'thank', gestures: [{ type: 'nod' }] }, actions: [{ type: 'wave', duration: 3 }, { type: 'dance' }] }, 'ありがとう。');
  assert.equal(performanceBodyIntent(plan).behaviorActions[0].type, 'wave');
  assert.equal(plan.bodyActions.length, 1); assert.equal(plan.bodyActions[0].durationMs, 3000);
  assert.equal(plan.gestures[0].type, 'nod');
  assert.equal(performanceBodyIntent(affect.plan({}, '大丈夫。')), null);
});

test('affect persists across lines, decays, and rejected/cancelled plans cannot alter it', () => {
  let time = 0; const affect = createAffectRuntime({ now: () => time });
  const p = affect.plan({}, 'ありがとう。'); const baseline = affect.snapshot().valence;
  assert.equal(affect.snapshot().valence, baseline); affect.commit(p);
  const happy = affect.snapshot().valence; assert(happy > baseline);
  const ordinary = affect.plan({ intent: 'neutral', gestures: [] }, '次の話をしよう。');
  assert.equal(ordinary.emotion, 'warm'); affect.commit(ordinary);
  assert(affect.snapshot().valence > baseline);
  const cancelled = affect.plan({ intent: 'refuse' }); affect.invalidate();
  assert.deepEqual(affect.commit(cancelled), affect.snapshot());
  const before = affect.snapshot(); affect.commit(p); assert.deepEqual(affect.snapshot(), before);
  time += 90000; assert(affect.snapshot().valence < before.valence);
});

test('anchored stroke needs exact final-text timing; short or changed audio never gets guessed anchors', () => {
  const affect = createAffectRuntime(), text = 'これは大切だよ。';
  const p = affect.plan({ gestures: [{ type: 'explain', anchorText: '大切' }] }, text);
  assert.equal(compilePerformanceTimeline(p, { duration: 5 }).cues.length, 0);
  const words = [{ startOffset: 3, endOffset: 5, start: 1.5 }];
  const cue = compilePerformanceTimeline(p, { duration: 5, words }).cues[0];
  assert.equal(cue.strokeMs, 1500); assert.equal(cue.startMs, 1180); assert.equal(cue.timingSource, 'word-alignment');
  assert.equal(compilePerformanceTimeline(p, { duration: 5, words, text: '追加。' + text }).cues.length, 0);
  const repeated = affect.plan({ gestures: [{ type: 'nod', anchorText: 'はい' }] }, 'はい、はい。');
  assert.equal(repeated.gestures[0].start, -1);
  assert.equal(compilePerformanceTimeline(affect.plan({}, 'ありがとう'), { duration: .2 }).cues.length, 0);
  const long = compilePerformanceTimeline(affect.plan({ gestures: [{ type: 'explain' }, { type: 'emphasis' }, { type: 'nod' }] }), { duration: 10 });
  assert.equal(long.cues.length, 3); assert(long.cues[1].startMs >= long.cues[0].startMs + long.cues[0].durationMs);
});

test('GPT-SoVITS receives bounded speed and retains the reference transcript verbatim', () => {
  const body = buildTtsRequest({ text: '大丈夫。', promptText: 'original reference transcript', speechStyle: { speed: .94, pitch: .3 } }).body;
  assert.equal(body.speed_factor, .94); assert.equal(body.prompt_text, 'original reference transcript'); assert.equal(body.pitch, undefined);
  assert.equal(buildTtsRequest({ text: 'hi', speechStyle: { speed: 4 } }).body.speed_factor, 1.1);
});

test('voice interruption requires sustained post-AEC energy and resets after noise or gaps', () => {
  const gate = createVoiceInterruptionGate();
  for (const t of [0, 85, 170]) assert.equal(gate.sample(.1, t), false);
  assert.equal(gate.sample(.001, 200), false);
  for (const t of [300, 385, 470, 555]) assert.equal(gate.sample(.1, t), false);
  assert.equal(gate.sample(.1, 640), true);
  gate.reset(); assert.equal(gate.sample(.1, 1000), false); assert.equal(gate.sample(.1, 1600), false);
  assert.equal(gate.sample(.02, 1700), false);
});
