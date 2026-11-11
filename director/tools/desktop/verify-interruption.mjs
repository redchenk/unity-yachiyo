// Installed page test: a local message interrupts two queued sentences and
// supplies its listener context to the next LLM request. No real LLM involved.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawn } from 'node:child_process';
import assert from 'node:assert/strict';
import { wav, vowelSamples } from './lipsync-fixtures.mjs';
const project = path.resolve(import.meta.dirname, '../../..');
const output = path.join(project, 'artifacts/performance-validation'); fs.mkdirSync(output, { recursive: true });
const script = path.join(output, 'interrupt-page.js'), nativeReport = script + '.native';
const bytes = wav(vowelSamples(0, 3));
fs.writeFileSync(script, `(async()=>{
const post=data=>window.webkit.messageHandlers.yachiyoVerification.postMessage(JSON.parse(JSON.stringify(data)));
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const wait=async(fn,label)=>{for(let i=0;i<500;i++){if(fn())return;await delay(40);}throw Error(label);};
const result={passed:false,plays:[],plans:[],requests:0,context:false};
try{
 await wait(()=>window.TSUKUYOMI_AVATAR_DRIVER?.ready,'avatar not ready');
 document.querySelector('.studio-settings-header button')?.click();
 localStorage.setItem('roomTTSSettings',JSON.stringify({enabled:true,provider:'fixture',useProxy:true}));
 localStorage.setItem('roomMemorySettings',JSON.stringify({enabled:false,retrievalMode:'off',writeMode:'off'}));
 localStorage.setItem('roomLLMSettings',JSON.stringify({provider:'custom',apiUrl:'http://fixture.invalid/v1/chat/completions',apiKey:'fixture',model:'fixture',useProxy:true}));
 const original=fetch, originalPlay=HTMLMediaElement.prototype.play, elements=[];
 const bytes=Uint8Array.from(atob('${bytes.toString('base64')}'),c=>c.charCodeAt(0));
 const oldVoice='今日は楽しい話をしよう。',oldQueued='次の話も用意しているよ。',newVoice='大丈夫、そばにいるよ。';
 window.addEventListener('yachiyo:performance-state',e=>result.plans.push(e.detail.plan));
 window.fetch=(url,options)=>{
  if(String(url)==='/api/tts')return Promise.resolve(new Response(bytes,{headers:{'Content-Type':'audio/wav'}}));
  if(String(url)==='/api/chat/stream'){
   result.requests++;const reassuring=result.requests>1;
   if(reassuring)result.context=options.body.includes('listener-distress')&&options.body.includes('performance');
   const voices=reassuring?[newVoice]:[oldVoice,oldQueued];
   const plan={intent:reassuring?'reassure':'explain',gestures:reassuring?[]:[{type:'explain'}]};
   const lines=voices.flatMap((voice,i)=>['BEAT: '+JSON.stringify({performance:plan,actions:[]}), 'VOICE: '+voice,'CAPTION: '+(reassuring?'没关系，我陪着你。':i?'我还准备了下一个话题。':'今天聊点开心的事吧。')]);
   lines.push('CONTROL: '+JSON.stringify({reply:voices.join(''),caption:reassuring?'没关系，我陪着你。':'今天聊点开心的事吧。我还准备了下一个话题。',performance:plan,actions:[],acknowledgedIndexes:reassuring?[1]:[],memory_writes:[]}));
   return Promise.resolve(new Response(lines.map(content=>'data: '+JSON.stringify({choices:[{delta:{content:content+'\\n'}}]})+'\\n\\n').join('')+'data: [DONE]\\n\\n',{headers:{'Content-Type':'text/event-stream'}}));
  }
  if(String(url)==='/api/chat')return Promise.resolve(new Response(JSON.stringify({success:true,data:{reply:'没关系，我陪着你。'}})));
  if(String(url).startsWith('/api/memory/'))return Promise.resolve(new Response(JSON.stringify({success:true,data:[],records:[]})));
  return original(url,options);
 };
 HTMLMediaElement.prototype.play=function(){
  if(this.src.startsWith('blob:')){elements.push(this);const entry={text:this.dataset.speechText,paused:false,ended:false};result.plays.push(entry);this.addEventListener('pause',()=>entry.paused=true);this.addEventListener('ended',()=>entry.ended=true);}
  return originalPlay.call(this);
 };
 [...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='开始直播').click();
 await wait(()=>result.plays.some(p=>p.text===oldVoice),'initial speech missing');await delay(400);
 const input=document.querySelector('input[placeholder="输入一条测试消息..."]');if(!input)throw Error('audience input missing');
 input.value='等一下，我今天有点难过。';input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
 await wait(()=>result.plays.some(p=>p.text?.includes(newVoice)),'new reply did not start');
 await delay(650);post({phase:'reassuring'});await delay(400);
 result.plays[0].paused=elements[0].paused;
 if(!result.plays[0].paused||result.plays[0].ended||elements[0].getAttribute('src'))throw Error('old speech was not interrupted');
 if(result.plays.some(p=>p.text===oldQueued))throw Error('cancelled queued sentence played');
 if(!result.context)throw Error('next generation missed listener context');
 if(!result.plans.some(p=>p.intent==='reassure'&&p.gestureAmount===0))throw Error('quiet reassurance missing');
 window.dispatchEvent(new CustomEvent('yachiyo:avatar-stop'));await delay(750);post({phase:'stopped'});await delay(350);
 const error=document.querySelector('.live2d-inline-error')?.textContent.trim();if(error)throw Error(error);
 result.passed=true;
}catch(e){window.dispatchEvent(new CustomEvent('yachiyo:avatar-stop'));result.error=String(e.message)+' '+String(e.stack||e);}
post({...result,done:true});
})();`);
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'yachiyo-interrupt-'));
for (const extension of ['.result.json', '.progress.json', '.native.json']) fs.rmSync(script + extension, { force: true });
const binary = path.join(os.homedir(), 'Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio');
const child = spawn(binary, ['--no-auto-connect', '--verify-director', nativeReport, '--verify-web-script', script, '-logFile', script + '.player.log'], { stdio: 'ignore', env: { ...process.env, YACHIYO_DATA_DIR: temp } });
let exited = false, exitCode; child.on('exit', code => { exited = true; exitCode = code; });
const phases = {};
try {
 const start = Date.now();
 while (!exited && Date.now() - start < 80000) {
  try { const progress = JSON.parse(fs.readFileSync(script + '.progress.json', 'utf8')); if (progress.phase && !phases[progress.phase]) phases[progress.phase] = JSON.parse(fs.readFileSync(nativeReport + '.json', 'utf8')); } catch {}
  await new Promise(r => setTimeout(r, 50));
 }
 assert(exited); assert.equal(exitCode, 0);
 const result = JSON.parse(fs.readFileSync(script + '.result.json', 'utf8'));
 fs.writeFileSync(path.join(output, 'interruption.json'), JSON.stringify({ ...result, phases }, null, 2));
 assert(result.passed, result.error); assert.equal(phases.reassuring.faceEmotion, 'ConcernedWarm'); assert.equal(phases.reassuring.gestureAmount, 0);
 assert.equal(phases.stopped.mouthOpen, 0); assert.equal(phases.stopped.speechBodyWeight, 0); assert.equal(phases.stopped.audioActive, false);
 console.log(JSON.stringify({ passed: true, oldQueuedSpeechDropped: true, contextForwarded: result.context, plans: result.plans.map(p => p.intent) }));
} finally { if (!exited) child.kill('SIGKILL'); fs.rmSync(temp, { recursive: true, force: true }); }
