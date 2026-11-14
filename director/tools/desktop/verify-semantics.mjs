// Real Japanese speech -> forced alignment + GPU StreamTalk -> Unity.
// A supplied WAV is replayed verbatim as the TTS response; no motion or timing
// response is stubbed. Run with a private Japanese WAV and its exact text.
import { build } from 'vite';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'../..'),project=path.dirname(root);
const [wavFile,configFile]=process.argv.slice(2);
if(!wavFile||!configFile)throw Error('Usage: node verify-semantics.mjs /absolute/voice.wav /absolute/semantic-plan.json');
const config=JSON.parse(fs.readFileSync(configFile,'utf8'));
assert(config.text&&config.performance?.semanticUnits?.length,'Provide exact Japanese text and semanticUnits');
const backend=process.env.YACHIYO_VERIFY_BACKEND||'desktop';
assert(['desktop','windows','ubuntu'].includes(backend));
const output=path.join(project,'artifacts/semantic-alignment/native');fs.mkdirSync(output,{recursive:true});
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'yachiyo-semantics-'));
const script=path.join(output,'browser.js'),nativeReport=path.join(output,'state');
const built=await build({configFile:false,logLevel:'error',build:{write:false,lib:{entry:path.join(root,'tools/desktop/avatar-browser-entry.mjs'),formats:['iife'],name:'AvatarFixture'}}});
const bundle=(Array.isArray(built)?built[0]:built).output.find(x=>x.type==='chunk').code;
fs.writeFileSync(script,bundle+`\n(async()=>{
const post=data=>window.webkit.messageHandlers.yachiyoVerification.postMessage(JSON.parse(JSON.stringify(data)));
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const wait=async(fn,label)=>{for(let i=0;i<1800;i++){if(fn())return;await delay(50);}throw Error(label);};
const result={passed:false,plans:[],jobs:[],motion:[]};let speech;
try{
 await wait(()=>window.TSUKUYOMI_AVATAR_DRIVER?.ready,'avatar not ready');
 document.querySelector('.studio-settings-header button')?.click();
 const driver=window.TSUKUYOMI_AVATAR_DRIVER;driver.control('backend',${JSON.stringify(backend)});driver.control('camera_reset');await delay(400);
 localStorage.setItem('yachiyo:speechMotion','on');
 localStorage.setItem('roomTTSSettings',JSON.stringify({enabled:true,provider:'fixture',useProxy:true}));
 const original=fetch,bytes=Uint8Array.from(atob('${fs.readFileSync(wavFile).toString('base64')}'),c=>c.charCodeAt(0));
 window.fetch=async(url,options)=>{
  if(String(url)==='/api/tts')return new Response(bytes,{headers:{'Content-Type':'audio/wav'}});
  const r=await original(url,options);
  if(String(url).includes('/api/speech-motion/jobs')&&options?.method==='POST')result.jobs.push(await r.clone().json());
  return r;
 };
 window.addEventListener('yachiyo:speech-motion',e=>result.motion.push(e.detail));
 const config=${JSON.stringify(config)};
 const affect=__avatarTools.affectRuntime;speech=__avatarTools.createLive2DSpeechPlayer();
 let audio,plan,finished=false;
 const play=speech.enqueue(config.text,{sourceLang:'ja',performance:affect.plan(config.performance,config.text),onStart:d=>{audio=d.audio;plan=d.performance;result.plans.push(plan);}});
 play.then(()=>{finished=true;},()=>{finished=true;});
 await wait(()=>audio,'speech did not start');
 if(plan.alignmentSource!=='qwen3-forced-aligner'||plan.cues.length!==config.performance.semanticUnits.filter(u=>u.gesture).length)throw Error('Missing actual aligned cues: '+JSON.stringify(plan));
 let paused=false,index=0;
 while(!finished){
  await delay(120);post({phase:'semantic-'+index++,audioTime:audio.currentTime});
  if(!paused&&audio.currentTime>(plan.cues[0].startMs+160)/1000){
   paused=true;audio.pause();await delay(300);post({phase:'paused0',audioTime:audio.currentTime});await delay(400);post({phase:'paused1',audioTime:audio.currentTime});await delay(250);await audio.play();
  }
 }
 await play;await delay(650);post({phase:'ended'});await delay(300);
 // No semantic unit means no invented local correction over the neural body.
 let quietAudio;
 const quiet=speech.enqueue(config.text,{sourceLang:'ja',performance:affect.plan({intent:'listen',semanticUnits:[]},config.text),onStart:d=>{quietAudio=d.audio;result.plans.push(d.performance);}});
 await wait(()=>quietAudio&&quietAudio.currentTime>.7,'quiet speech did not play');post({phase:'quiet'});await delay(300);speech.stop();await quiet.catch(()=>{});await delay(650);post({phase:'cancelled'});await delay(300);
 result.passed=true;
}catch(error){speech?.stop();result.error=String(error.stack||error);}
post({...result,done:true});
})();`);
for(const file of [script+'.progress.json',script+'.result.json',nativeReport+'.json'])fs.rmSync(file,{force:true});
const binary=process.env.YACHIYO_UNITY_BINARY||path.join(os.homedir(),'Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio');
const child=spawn(binary,['--no-auto-connect','--motion-platform',backend,'--verify-director',nativeReport,'--verify-web-script',script,'-logFile',path.join(output,'player.log')],{stdio:'ignore',env:{...process.env,YACHIYO_DATA_DIR:temp}});
let exited=false;child.on('exit',()=>exited=true);const phases={},start=Date.now();
try{
 while(!exited&&Date.now()-start<180000){
  try{const p=JSON.parse(fs.readFileSync(script+'.progress.json','utf8'));if(p.phase&&!phases[p.phase])phases[p.phase]={...JSON.parse(fs.readFileSync(nativeReport+'.json','utf8')),mediaTime:p.audioTime};}catch{}
  await new Promise(r=>setTimeout(r,50));
 }
 assert(exited,'Native semantic test timed out');
 const result=JSON.parse(fs.readFileSync(script+'.result.json','utf8'));
 fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({result,phases},null,2));
 assert(result.passed,result.error);
 const samples=Object.entries(phases).filter(([key])=>key.startsWith('semantic')).map(([,p])=>p);
 const corrected=samples.filter(p=>p.semanticCorrection>.1);
 assert(corrected.length>0,'Semantic correction was dropped while neural motion owned the body');
 assert(corrected.every(p=>p.neuralSpeechMotion&&p.motionSource.includes('StreamTalk')),'Semantic correction replaced the generated body');
 assert(Math.max(...samples.map(p=>p.maxSemanticCorrection))<=7,'Correction exceeded the local bound');
 for(const cue of result.plans[0].cues){
  const sample=samples.find(p=>p.semanticUnit===cue.semanticId);
  assert(sample,'Native did not execute '+cue.semanticId);
  assert(sample.lastCueClock>=cue.startMs&&sample.lastCueClock<=cue.strokeMs+180,'Cue outside its media-clock window');
  assert.equal(sample.cueTiming,'word-alignment');
 }
 const angle=(a,b)=>2*Math.acos(Math.min(1,Math.abs(a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w)))*180/Math.PI;
 assert(Math.abs(phases.paused0.renderedClock-phases.paused1.renderedClock)<.001,'Media clock advanced while paused');
 assert(angle(phases.paused0.headLocal,phases.paused1.headLocal)<.05,'Semantic head moved while paused');
 assert.equal(phases.quiet.cuesFired,result.plans[0].cues.length,'Quiet line added a cue');assert.equal(phases.quiet.semanticCorrection,0);
 assert.equal(phases.cancelled.audioActive,false);assert.equal(phases.cancelled.neuralSpeechMotion,false);assert.equal(phases.cancelled.semanticCorrection,0);
 console.log(JSON.stringify({passed:true,alignedCues:result.plans[0].cues,correctedFrames:phases.ended.semanticFrames,maxCorrection:phases.ended.maxSemanticCorrection,pauseStable:true,cancelled:true}));
}finally{if(!exited)child.kill('SIGTERM');fs.rmSync(temp,{recursive:true,force:true});}
