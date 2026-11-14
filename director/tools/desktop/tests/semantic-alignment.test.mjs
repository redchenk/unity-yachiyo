import test from 'node:test';
import assert from 'node:assert/strict';
import { createAffectRuntime, compilePerformanceTimeline, performancePrompt } from '../../../src/frontend/services/room/performancePlan.mjs';
import { rebindPerformance, parseTranslatedPerformance } from '../../../src/frontend/services/room/bilingualPerformance.mjs';
import { prepareSpeechMotion } from '../../../src/frontend/services/room/speechMotion.mjs';
import { createSpeechMotionBuffer } from '../../../src/frontend/services/room/speechMotionBuffer.mjs';
const plan = () => createAffectRuntime().plan({ intent: 'reassure', semanticUnits: [
  { id: 'listen', intent: 'listen', sourceText: '我在听', spokenText: '聞いているよ', gesture: 'nod' },
  { id: 'invite', intent: 'reassure', sourceText: '慢慢说', spokenText: 'ゆっくり話して', gesture: 'open_palms' }
]}, '大丈夫。ゆっくり話して。聞いているよ。');

test('meaning units bind to Japanese spans even when their order differs from Chinese', () => {
  const p=plan();
  const words=[{startOffset:0,endOffset:3,start:.1,end:.8},{startOffset:4,endOffset:11,start:1.2,end:2.8},{startOffset:12,endOffset:18,start:3.2,end:4.5}];
  const timeline=compilePerformanceTimeline(p,{duration:5,words,alignmentSource:'qwen3-forced-aligner'});
  assert.equal(timeline.cues.length,2);
  assert.equal(timeline.cues.find(c=>c.semanticId==='invite').strokeMs,1200);
  assert.equal(timeline.cues.find(c=>c.semanticId==='listen').strokeMs,3200);
  assert.equal(timeline.alignmentSource,'qwen3-forced-aligner');
  assert.equal(p.semanticUnits[0].sourceText,'我在听');
  assert(performancePrompt().includes('same negation'));
});

test('translation binds by stable meaning id and invalidates omitted or ambiguous meanings', () => {
  const p=plan(), text='聞いているよ。大丈夫。';
  const translated=rebindPerformance(p,text,[{id:'listen',spokenText:'聞いているよ'},{id:'unknown',spokenText:'大丈夫'}]);
  assert.equal(translated.gestures[0].start,0);
  assert.equal(translated.gestures[1].start,-1);
  assert.equal(translated.semanticUnits[0].sourceText,'我在听');
  const ambiguous=rebindPerformance(p,'はい、はい。',[{id:'listen',spokenText:'はい'}]);
  assert.equal(ambiguous.gestures[0].start,-1);
  assert.equal(compilePerformanceTimeline(p,{duration:5,text}).cues.length,0);
  assert.equal(parseTranslatedPerformance('bad JSON',p,x=>x),null);
  assert.equal(parseTranslatedPerformance(JSON.stringify({text,bindings:[{id:'listen',spokenText:'聞いているよ'}]}),p,x=>x).performance.gestures[0].start,0);
});

test('quiet units, absent timing, repeated words and prohibited gestures cannot invent a correction', () => {
  const affect=createAffectRuntime();
  for(const gesture of [undefined,'head_tilt','point_at_unknown']){
    const p=affect.plan({semanticUnits:[{id:'quiet',intent:'listen',spokenText:'聞いている',gesture}]},'聞いている');
    assert.equal(p.gestures.length,0);
  }
  assert.equal(compilePerformanceTimeline(plan(),{duration:6}).cues.length,0);
  const repeated=affect.plan({semanticUnits:[{id:'a',intent:'explain',spokenText:'はい',gesture:'nod'}]},'はい、はい');
  assert.equal(repeated.gestures[0].start,-1);
  const nearStart=affect.plan({semanticUnits:[{id:'a',intent:'thank',spokenText:'ありがとう',gesture:'nod'}]},'ありがとう。');
  const timeline=compilePerformanceTimeline(nearStart,{duration:2,words:[{startOffset:0,endOffset:5,start:.05,end:1.2}]});
  assert.equal(timeline.cues.length,1);assert.equal(timeline.cues[0].startMs,0);assert.equal(timeline.cues[0].strokeMs,140);
});

const buffer={duration:1,sampleRate:16000,numberOfChannels:1,getChannelData:()=>new Float32Array(16000)};
test('the exact decoded audio and final Japanese text share the motion job and its alignment', async()=>{
  let input;
  const alignment={text:'聞いているよ',source:'qwen3-forced-aligner',words:[{start:.2,end:.9,startOffset:0,endOffset:6}]};
  const motion=await prepareSpeechMotion(buffer,{spokenText:alignment.text,fetchImpl:async(url,options)=>{
    if(options.method==='POST'){input=JSON.parse(options.body);return new Response(JSON.stringify({id:'abcdef'}));}
    if(options.method==='DELETE')return new Response('{}');
    return new Response(JSON.stringify({status:'done',availableFrames:30,motion:{startFrame:0,frames:Array(30).fill({})},alignment,alignmentStatus:'ready'}));
  }});
  assert.equal(input.spokenText,alignment.text);assert.equal(input.sampleRate,16000);
  assert.equal(atob(input.pcm16).length,32000);assert.deepEqual(motion.alignment,alignment);motion.stop();
});

test('revised final text cannot reuse cached alignment from the previous meaning', async()=>{
  const requests=[];let stops=0;
  const cache=createSpeechMotionBuffer({decode:async()=>buffer,backend:()=> 'desktop',enabled:()=>true,prepare:async(pcm,options)=>{requests.push(options.spokenText);return{stop(){stops++;}};}});
  const audio={};await cache.get(audio,{spokenText:'大丈夫'});await cache.get(audio,{spokenText:'違うよ'});
  assert.deepEqual(requests,['大丈夫','違うよ']);assert.equal(stops,1);cache.release(audio);
});

test('same-arm units use spoken order, and comforting cues are bounded even if the LLM overstates them',()=>{
  const p=createAffectRuntime().plan({intent:'reassure',semanticUnits:[
    {id:'late',intent:'reassure',spokenText:'聞いて',gesture:'explain',strength:1},
    {id:'early',intent:'reassure',spokenText:'大丈夫',gesture:'open_palms',strength:1}
  ]},'大丈夫、聞いて');
  const compiled=compilePerformanceTimeline(p,{duration:7,words:[{startOffset:0,endOffset:3,start:1,end:2},{startOffset:4,endOffset:7,start:4,end:5}]});
  assert.deepEqual(compiled.cues.map(c=>c.semanticId),['early','late']);
  assert(compiled.cues.every(c=>c.intensity<=.4));
});

test('plain Japanese translation can still speak without reusing Chinese gesture offsets',()=>{
  const translated=parseTranslatedPerformance('ゆっくり話そう。',plan(),x=>x);
  assert.equal(translated.text,'ゆっくり話そう。');
  assert(translated.performance.gestures.every(g=>g.start===-1));
  assert.equal(compilePerformanceTimeline(translated.performance,{duration:3}).cues.length,0);
});

test('structured conversation control preserves bilingual semantic units through the actual parser', async()=>{
  const {createServer}=await import('vite');
  const server=await createServer({configFile:false,root:new URL('../../../',import.meta.url).pathname,server:{middlewareMode:true,hmr:false},appType:'custom'});
  const oldWindow=globalThis.window;
  globalThis.window={TSUKUYOMI_AVATAR_DRIVER:{mode:'unity'}};
  try{
    const {parseLive2DControlPayload,live2DStreamingControlSystemPrompt}=await server.ssrLoadModule('/src/frontend/services/room/live2dLlmControl.js');
    const parsed=parseLive2DControlPayload(JSON.stringify({reply:'ゆっくり話して。',caption:'慢慢说。',performance:{intent:'reassure',semanticUnits:[{id:'invite',intent:'reassure',sourceText:'慢慢说',spokenText:'ゆっくり話して',gesture:'open_palms'}]}}));
    assert.equal(parsed.performance.semanticUnits[0].sourceText,'慢慢说');
    assert.equal(parsed.performance.gestures[0].anchorText,'ゆっくり話して');
    assert.equal(parsed.performance.gestures[0].semanticId,'invite');
    assert(live2DStreamingControlSystemPrompt().includes('semanticUnits'));
  }finally{globalThis.window=oldWindow;await server.close();}
});
