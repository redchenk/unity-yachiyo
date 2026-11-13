// Actual WKWebView audio -> semantic plan -> native Unity face/gaze/body clock.
import { build } from 'vite';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
import { wav, vowelSamples } from './lipsync-fixtures.mjs';
const root = path.resolve(import.meta.dirname, '../..'), project = path.dirname(root);
const output = path.join(project, 'artifacts/performance-validation'); fs.mkdirSync(output, { recursive: true });
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'yachiyo-performance-'));
const script = path.join(output, 'browser-test.js'), nativeReport = path.join(output, 'native');
const built = await build({ configFile: false, logLevel: 'error', build: { write: false, lib: { entry: path.join(root, 'tools/desktop/avatar-browser-entry.mjs'), formats: ['iife'], name: 'AvatarFixture' } } });
const bundle = (Array.isArray(built) ? built[0] : built).output.find(x => x.type === 'chunk').code;
const bytes = wav(vowelSamples(0, 2.2));
const explicitGestures = {thank:'nod',reassure:'open_palms',explain:'explain',tease:'emphasis',surprise:'open_palms',refuse:'shake_head'};
const cases = [
 ['thank', 'ありがとう。うれしいよ。', 'Shy'], ['reassure', '大丈夫、そばにいるよ。', 'ConcernedWarm'],
 ['explain', '例えば、こういうことだよ。', 'Warm'], ['tease', '冗談だよ。', 'Playful'],
 ['surprise', 'まさか、本当に？', 'Surprised'], ['refuse', 'それはお断りするね。', 'Serious'],
 ['listen', '聞いているよ。', 'Warm']
];
fs.writeFileSync(script, bundle + `\n(async()=>{
const post=data=>window.webkit.messageHandlers.yachiyoVerification.postMessage(JSON.parse(JSON.stringify(data)));
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const wait=async(fn,label)=>{for(let i=0;i<300;i++){if(fn())return;await delay(30);}throw Error(label);};
const result={passed:false,plans:[]};
try{
 await wait(()=>window.TSUKUYOMI_AVATAR_DRIVER?.ready,'avatar not ready');
 document.querySelector('.studio-settings-header button')?.click();
 const driver=window.TSUKUYOMI_AVATAR_DRIVER;
 const original=fetch, bytes=Uint8Array.from(atob('${bytes.toString('base64')}'),c=>c.charCodeAt(0));
 window.fetch=(url,options)=>String(url)==='/api/tts'?Promise.resolve(new Response(bytes,{headers:{'Content-Type':'audio/wav'}})):original(url,options);
 localStorage.setItem('yachiyo:speechMotion','off');
 localStorage.setItem('roomTTSSettings',JSON.stringify({enabled:true,provider:'fixture',useProxy:true}));
 const speech=__avatarTools.createLive2DSpeechPlayer(),affect=__avatarTools.affectRuntime;
 driver.control('camera_reset'); await delay(600);post({phase:'idle'});await delay(350);
 if(window.TSUKUYOMI_AVATAR_CAPABILITIES?.actions?.includes('head_tilt'))throw Error('head tilt still advertised');
 if(__avatarTools.live2DStreamingControlSystemPrompt().includes('head_tilt'))throw Error('head tilt still prompted');
 for(const raw of [JSON.stringify({reply:'歪头想一想',emotion:'shy',actions:[]}), '微笑着歪头思考。']){
  const parsed=__avatarTools.parseLive2DControlPayload(raw);
  if(parsed.live2d?.behaviorActions?.length||parsed.live2d?.bodyPose)throw Error('legacy parser invented a body action');
 }
 for(const mode of ['thinking','listening']){
  driver.state({mode,holdMs:5000});await delay(900);post({phase:mode});await delay(350);
 }
 driver.perform({behaviorActions:[{type:'head_tilt'}],bodyMode:'local',durationMs:1000});
 await delay(700);post({phase:'rejectedTilt'});await delay(350);
 driver.state({mode:'idle'});
 let quietStarted=false;
 const quietPlay=speech.enqueue('少し考えているよ。',{performance:affect.plan({intent:'tease'}),onStart:()=>{quietStarted=true;}});
 await wait(()=>quietStarted,'quiet speech not started');await delay(700);post({phase:'implicitQuiet'});await delay(350);
 await quietPlay;await delay(650);post({phase:'implicitQuietEnded'});await delay(350);
 const explicitGestures=${JSON.stringify(explicitGestures)};
 for(const [intent,text] of ${JSON.stringify(cases)}){
  const plan=affect.plan({performance:{intent,gestures:explicitGestures[intent]?[{type:explicitGestures[intent]}]:[]}},text);
  let started=false;
  const play=speech.enqueue(text,{sourceLang:'ja',performance:plan,onStart:({performance})=>{started=true;result.plans.push(performance);}});
  await wait(()=>started,intent+' did not start');await delay(620);post({phase:intent});await delay(250);
  await play;await delay(600);post({phase:intent+'-ended'});await delay(250);
 }
 let started=false;
 const stopped=speech.enqueue('説明の途中です。',{sourceLang:'ja',performance:affect.plan({intent:'explain',gestures:[{type:'explain'}]},'説明の途中です。'),onStart:()=>{started=true;}}).catch(e=>e.name);
 await wait(()=>started,'cancel test did not start');await delay(350);speech.stop();
 if(await stopped!=='AbortError')throw Error('stop did not reject old speech');
 await delay(650);post({phase:'cancelled'});await delay(350);result.passed=true;
}catch(e){result.error=String(e.stack||e);}
post({...result,done:true});
})();`);
for (const file of [script + '.progress.json', script + '.result.json', nativeReport + '.json']) fs.rmSync(file, { force: true });
const binary = process.env.YACHIYO_UNITY_BINARY || path.join(os.homedir(), 'Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio');
const child = spawn(binary, ['--no-auto-connect', '--verify-director', nativeReport, '--verify-web-script', script, '-logFile', path.join(output, 'player.log')], { stdio: 'ignore', env: { ...process.env, YACHIYO_DATA_DIR: temp } });
let exited = false, exitCode; child.on('exit', code => { exited = true; exitCode = code; });
const phases = {}, start = Date.now();
try {
 while (!exited && Date.now() - start < 90000) {
  try { const progress = JSON.parse(fs.readFileSync(script + '.progress.json', 'utf8')); if (progress.phase && !phases[progress.phase]) phases[progress.phase] = JSON.parse(fs.readFileSync(nativeReport + '.json', 'utf8')); } catch {}
  await new Promise(r => setTimeout(r, 50));
 }
 assert(exited, 'application did not exit'); assert.equal(exitCode, 0);
 const result = JSON.parse(fs.readFileSync(script + '.result.json', 'utf8'));
 fs.writeFileSync(path.join(output, 'phases.json'), JSON.stringify({ phases, result }, null, 2));
 assert(result.passed, result.error);
 const angle=(a,b)=>2*Math.acos(Math.min(1,Math.abs(a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w)))*180/Math.PI;
 const roll=q=>Math.atan2(2*(q.w*q.z+q.x*q.y),1-2*(q.y*q.y+q.z*q.z))*180/Math.PI;
 for(const name of ['thinking','listening','rejectedTilt','implicitQuiet']){
  assert.equal(phases[name].followPointer,false);
  // Natural idle now permits small breathing/attention motion, never a thinking tilt.
  assert(Math.abs(roll(phases.idle.headLocal)-roll(phases[name].headLocal))<.15,name+' tilted head');
  assert(angle(phases.idle.headLocal,phases[name].headLocal)<3,name+' exceeded idle head attention');
  assert(angle(phases.idle.neckLocal,phases[name].neckLocal)<2,name+' exceeded idle neck attention');
  assert.equal(phases[name].cuesFired,0);
 }
 assert.equal(phases.thinking.characterState,'thinking');
 assert.equal(phases.rejectedTilt.lastReceipt,'unsupported');
 assert.equal(phases.implicitQuiet.gestureAmount,0);
 assert.equal(phases.implicitQuietEnded.mouthOpen,0);
 let previous = 0;
 for (const [intent, , face] of cases) {
  const native = phases[intent], ended = phases[intent + '-ended'];
  assert.equal(native.performanceIntent, intent); assert.equal(native.faceEmotion, face);
  assert(native.performanceClock > 250 && native.performanceClock < 1000);
  assert.equal(ended.faceEmotion, face, 'affect must survive the utterance');
  assert.equal(ended.mouthOpen, 0); assert.equal(ended.speechPlaybackWeight, 0);
  if (intent === 'listen') { assert.equal(native.gestureAmount, 0); assert.equal(native.cuesFired, previous); }
  else { assert(native.cuesFired > previous); assert(native.lastCueClock < 250, 'onset cue arrived late'); }
  previous = native.cuesFired;
 }
 assert.equal(phases.cancelled.mouthOpen, 0); assert.equal(phases.cancelled.speechPlaybackWeight, 0); assert.equal(phases.cancelled.audioActive, false);
 const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
 const expressive = distance(phases.explain.rightHand, phases['explain-ended'].rightHand);
 const quiet = distance(phases.listen.rightHand, phases['listen-ended'].rightHand);
 assert(expressive > .025, 'explanation did not move the arm'); assert(quiet < expressive * .5, 'quiet listening still waved its arms');
 fs.writeFileSync(path.join(output, 'verification.json'), JSON.stringify({ passed: true, expressiveArmMeters: expressive, quietArmMeters: quiet, phases, plans: result.plans }, null, 2));
 console.log(JSON.stringify({ passed: true, scenarios: cases.length, expressiveArmMeters: expressive, quietArmMeters: quiet, automaticHeadTilt: false, implicitSpeechGestures: false, cancelled: true }));
} finally { if (!exited) child.kill('SIGKILL'); fs.rmSync(temp, { recursive: true, force: true }); }
