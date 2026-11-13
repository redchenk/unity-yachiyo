// Requires the real Windows/Ubuntu StreamTalk service. Only the TTS response is
// supplied from a WAV; all body frames must come from live GPU inference.
import { build } from 'vite';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';

const root=path.resolve(import.meta.dirname,'../..'),project=path.dirname(root);
const wavFile=process.argv[2];
if(!wavFile)throw Error('Usage: node tools/desktop/verify-streamtalk.mjs /absolute/path/spoken.wav');
const bytes=fs.readFileSync(wavFile),output=path.join(project,'artifacts/streamtalk-native');fs.mkdirSync(output,{recursive:true});
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'yachiyo-streamtalk-'));
const script=path.join(output,'browser-test.js'),nativeReport=path.join(output,'native');
const built=await build({configFile:false,logLevel:'error',build:{write:false,lib:{entry:path.join(root,'tools/desktop/avatar-browser-entry.mjs'),formats:['iife'],name:'AvatarFixture'}}});
const bundle=(Array.isArray(built)?built[0]:built).output.find(x=>x.type==='chunk').code;
fs.writeFileSync(script,bundle+`\n(async()=>{
const post=data=>window.webkit.messageHandlers.yachiyoVerification.postMessage(JSON.parse(JSON.stringify(data)));
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const wait=async(fn,label)=>{for(let i=0;i<2000;i++){if(fn())return;await delay(50);}throw Error(label);};
const result={passed:false,motionStatuses:[],jobs:[],audioTimes:[]};let speech;
try{
 await wait(()=>window.TSUKUYOMI_AVATAR_DRIVER?.ready,'avatar not ready');
 document.querySelector('.studio-settings-header button')?.click();
 const driver=window.TSUKUYOMI_AVATAR_DRIVER;driver.control('backend','windows');driver.control('camera_reset');await delay(600);
 localStorage.setItem('yachiyo:speechMotion','on');
 localStorage.setItem('roomTTSSettings',JSON.stringify({enabled:true,provider:'fixture',useProxy:true}));
 const original=fetch,bytes=Uint8Array.from(atob('${bytes.toString('base64')}'),c=>c.charCodeAt(0));
 window.fetch=async(url,options)=>{
  if(String(url)==='/api/tts')return new Response(bytes,{headers:{'Content-Type':'audio/wav'}});
  const r=await original(url,options);
  if(String(url).includes('/api/speech-motion/jobs')&&options?.method==='POST')result.jobs.push(await r.clone().json());
  return r;
 };
 window.addEventListener('yachiyo:speech-motion',e=>result.motionStatuses.push(e.detail));
 speech=__avatarTools.createLive2DSpeechPlayer();let audio;
 const play=speech.enqueue('声と体の動きを確認します。',{sourceLang:'ja',performance:__avatarTools.affectRuntime.plan({intent:'explain',gestures:[{type:'explain'}]}),onStart:d=>{audio=d.audio;}});
 await wait(()=>audio,'speech never started');
 // Let the audio device start and the one-shot diagnostic PNG finish before
 // measuring display cadence. Real audio stalls must hold, not invent motion.
 await wait(()=>audio.currentTime>=1.2,'audio clock never advanced');
 for(let i=0;i<5;i++){await delay(500);result.audioTimes.push(audio.currentTime);post({phase:'speaking'+i,audioTime:audio.currentTime});await delay(220);}
 audio.pause();await delay(400);post({phase:'paused0',audioTime:audio.currentTime});await delay(400);post({phase:'paused1',audioTime:audio.currentTime});await delay(220);
 await audio.play();await delay(700);post({phase:'resumed',audioTime:audio.currentTime});await delay(220);
 await play;await delay(650);post({phase:'ended'});await delay(250);
 const continuation=speech.enqueue('続けて話します。',{sourceLang:'ja'});
 let second=false;const cancelled=speech.enqueue('途中で停止します。',{sourceLang:'ja',onStart:()=>{second=true;}}).catch(e=>e.name);
 await continuation;await delay(200);post({phase:'joining'});await delay(220);
 await wait(()=>second,'second speech never started');await delay(500);speech.stop();
 if(await cancelled!=='AbortError')throw Error('Cancellation did not reject old speech');
 await delay(650);post({phase:'cancelled'});await delay(250);
 if(result.motionStatuses.some(s=>s.status==='error'))throw Error(JSON.stringify(result.motionStatuses));
 if(result.jobs.length<3||result.jobs.some(j=>!j.id))throw Error('Real inference jobs not observed');
 result.passed=true;
}catch(e){speech?.stop();result.error=String(e.stack||e);}
post({...result,done:true});
})();`);
for(const file of [script+'.progress.json',script+'.result.json',nativeReport+'.json'])fs.rmSync(file,{force:true});
const binary=process.env.YACHIYO_UNITY_BINARY||path.join(os.homedir(),'Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio');
const child=spawn(binary,['--no-auto-connect','--motion-platform','windows','--verify-director',nativeReport,'--verify-web-script',script,'-logFile',path.join(output,'player.log')],{stdio:'ignore',env:{...process.env,YACHIYO_DATA_DIR:temp}});
let exited=false;child.on('exit',()=>exited=true);
const phases={},start=Date.now();
try{
 while(!exited&&Date.now()-start<240000){
  try{const p=JSON.parse(fs.readFileSync(script+'.progress.json','utf8'));if(p.phase&&!phases[p.phase])phases[p.phase]={...JSON.parse(fs.readFileSync(nativeReport+'.json','utf8')),mediaTime:p.audioTime};}catch{}
  await new Promise(r=>setTimeout(r,50));
 }
 assert(exited,'Native StreamTalk test timed out');
 const result=JSON.parse(fs.readFileSync(script+'.result.json','utf8'));
 fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({result,phases},null,2));
 assert(result.passed,result.error);
 const speaking=Object.entries(phases).filter(([name])=>name.startsWith('speaking')).map(([,v])=>v);
 assert.equal(speaking.length,5);
 for(const p of speaking){assert(p.neuralSpeechMotion&&p.bodyPlaying);assert(p.motionSource.includes('StreamTalk'));assert.equal(p.cuesFired,0);assert(Math.abs(p.neuralClock-p.mediaTime)<.3);assert.equal(p.neuralUnderruns,0);}
 const distance=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);
 const first=speaking[0],last=speaking.at(-1);
 assert(last.neuralInterpolatedFrames-first.neuralInterpolatedFrames>20,'No render-cadence interpolation between audio clock reports');
 assert((last.neuralPoseAdvances-first.neuralPoseAdvances)/(last.neuralRenderFrames-first.neuralRenderFrames)>.85,'Generated body poses are holding display frames');
 assert(Math.max(...speaking.map(p=>distance(p.rightHand,speaking[0].rightHand)))>.015,'No generated hand movement');
 assert(Math.abs(phases.paused0.neuralClock-phases.paused1.neuralClock)<.04,'Motion advanced while paused');
 assert(Math.abs(phases.paused0.renderedClock-phases.paused1.renderedClock)<.001,'Interpolated motion advanced while paused');
 assert(phases.resumed.neuralClock>phases.paused1.neuralClock+.3,'Motion did not resume with audio');
 assert(phases.joining.awaitingContinuation,'Queued speech did not retain continuation intent');
 assert.equal(phases.joining.poseMode,'joining','Returned to idle while the next utterance was preparing');
 assert.equal(phases.cancelled.idleDuringContinuationFrames,0,'Queued generated speech passed through idle');
 for(const name of ['ended','cancelled']){assert.equal(phases[name].neuralSpeechMotion,false);assert.equal(phases[name].bodyPlaying,false);assert.equal(phases[name].audioActive,false);}
 console.log('PASS: real StreamTalk inference → WKWebView audio → Unity media-clock body, pause, resume and cancel');
 console.log(path.join(output,'result.json'));
}finally{if(!exited)child.kill('SIGTERM');}
