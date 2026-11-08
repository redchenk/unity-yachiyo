// Test the installed page (not an injected speech implementation) in WKWebView.
// Optional --settings-file points to private GPT-SoVITS settings, never logged.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawn } from 'node:child_process';
import assert from 'node:assert/strict';
import { lipsyncFixture } from './lipsync-fixtures.mjs';

const project = path.resolve(import.meta.dirname, '../../..');
const output = path.join(project, 'artifacts/tts-verification'); fs.mkdirSync(output, { recursive: true });
const option = process.argv.indexOf('--settings-file');
const live = option >= 0;
const settings = live ? JSON.parse(fs.readFileSync(process.argv[option + 1], 'utf8')) : { enabled: true, provider: 'gpt-sovits', apiUrl: 'http://127.0.0.1:3289/tts' };
assert.equal(settings.provider, 'gpt-sovits');
const fixture = lipsyncFixture().wav; fixture.writeUInt32LE(36, 4); fixture.writeUInt32LE(0, 40);
const script = path.join(output, live ? 'live-provider.js' : 'stream-fixture.js'), nativeReport = script + '.native';
fs.writeFileSync(script, `(async()=>{
const post=data=>window.webkit.messageHandlers.yachiyoVerification.postMessage(JSON.parse(JSON.stringify(data)));
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const wait=async(fn,label)=>{for(let i=0;i<800;i++){if(fn())return;await delay(50);}throw Error(label);};
const result={done:false,passed:false,source:${JSON.stringify(live ? 'live GPT-SoVITS, actual installed page' : 'zero-length streaming WAV fixture, actual installed page')},plays:[]};
const progress=setInterval(()=>post({...result,phase:'playback'}),500);
try{
 await wait(()=>window.TSUKUYOMI_AVATAR_DRIVER?.ready,'avatar not ready');
 document.querySelector('.studio-settings-header button')?.click();
 localStorage.setItem('roomTTSSettings',JSON.stringify(${JSON.stringify({ ...settings, enabled: true })}));
 localStorage.setItem('roomMemorySettings',JSON.stringify({enabled:false,retrievalMode:'off',writeMode:'off'}));
 localStorage.setItem('roomLLMSettings',JSON.stringify({provider:'custom',apiUrl:'http://fixture.invalid/v1/chat/completions',apiKey:'test-fixture',model:'fixture',useProxy:true}));
 if(!${live}){localStorage.setItem('roomAudioLipsyncSettings',JSON.stringify({profile:${JSON.stringify(lipsyncFixture().profile)}}));window.dispatchEvent(new Event('tsukuyomi:lipsync-settings'));}
 const originalFetch=window.fetch, originalPlay=HTMLMediaElement.prototype.play;
 let chatRequests=0;result.requests=[];
 const voice='こんにちは。音声のテストです。',caption='你好，这是语音播放测试。';
 const lines=['BEAT: {"emotion":"happy","actions":[]}', 'VOICE: '+voice, 'CAPTION: '+caption, 'CONTROL: '+JSON.stringify({reply:voice,caption,emotion:'happy',actions:[],memory_writes:[]})];
 const packets=lines.map(content=>'data: '+JSON.stringify({choices:[{delta:{content:content+'\\n'}}]})+'\\n\\n').join('')+'data: [DONE]\\n\\n';
 const bytes=Uint8Array.from(atob('${fixture.toString('base64')}'),c=>c.charCodeAt(0));
 window.fetch=(url,options)=>{
  const target=String(url);
  if(target.startsWith('/api/'))result.requests.push(target.split('?')[0]);
  if(target==='/api/chat/stream'){chatRequests++;return Promise.resolve(new Response(packets,{headers:{'Content-Type':'text/event-stream'}}));}
  if(target==='/api/chat')return Promise.resolve(new Response(JSON.stringify({success:true,data:{reply:voice}}),{headers:{'Content-Type':'application/json'}}));
  if(target.startsWith('/api/memory/'))return Promise.resolve(new Response(JSON.stringify({success:true,data:[],records:[]} ),{headers:{'Content-Type':'application/json'}}));
  if(!${live}){
   if(target==='/api/tts/prepare')return Promise.resolve(new Response(JSON.stringify({url:'/api/tts/stream?token=fixture'})));
   if(target.startsWith('/api/tts/stream?'))return Promise.resolve(new Response(new ReadableStream({start(controller){controller.enqueue(bytes.subarray(0,44));setTimeout(()=>{controller.enqueue(bytes.subarray(44));controller.close();},100);}}),{headers:{'Content-Type':'audio/wav'}}));
  }
  return originalFetch(url,options);
 };
 HTMLMediaElement.prototype.play=function(){
  if(!this.src.startsWith('blob:')&&!this.src.includes('/api/tts/stream'))return originalPlay.call(this);
  const entry={duration:0,maxTime:0,ended:false,blob:this.src.startsWith('blob:')};result.plays.push(entry);
  this.addEventListener('timeupdate',()=>{if(Number.isFinite(this.duration))entry.duration=this.duration;entry.maxTime=Math.max(entry.maxTime,this.currentTime);});
  this.addEventListener('ended',()=>{entry.ended=true;entry.maxTime=Math.max(entry.maxTime,this.currentTime);});
  this.addEventListener('error',()=>{entry.error=this.error?.code;});
  return originalPlay.call(this).catch(e=>{entry.error=e.name+': '+e.message;throw e;});
 };
 const start=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='开始直播');
 if(!start)throw Error('live UI start button missing'); start.click();
 await wait(()=>result.plays.some(p=>p.ended)||result.plays.some(p=>p.error),'real page speech did not play');
 const played=result.plays.find(p=>p.ended);
 if(!played||played.error||!played.blob||played.maxTime<.5||played.duration<.5)throw Error('audio failed: '+JSON.stringify(result.plays));
 result.chatRequests=chatRequests;
 window.dispatchEvent(new CustomEvent('yachiyo:avatar-stop'));
 await delay(1200);
 result.inlineError=document.querySelector('.live2d-inline-error')?.textContent.trim()||'';
 if(result.inlineError)throw Error(result.inlineError);
 result.passed=true;
}catch(e){window.dispatchEvent(new CustomEvent('yachiyo:avatar-stop'));result.error=e.name+': '+e.message+' '+(e.stack||'');}
clearInterval(progress);
post({...result,done:true});
})().catch(e=>window.webkit.messageHandlers.yachiyoVerification.postMessage({done:true,passed:false,error:String(e)}));`);

const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'yachiyo-tts-'));
const binary = process.env.YACHIYO_UNITY_BINARY || path.join(os.homedir(), 'Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio');
for (const extension of ['.result.json', '.progress.json', '.native.json']) fs.rmSync(script + extension, { force: true });
const child = spawn(binary, ['--no-auto-connect', '--verify-director', nativeReport, '--verify-web-script', script, '-logFile', script + '.player.log'], { stdio: 'ignore', env: { ...process.env, YACHIYO_DATA_DIR: temp } });
let exited = false, exitCode, exitSignal; child.on('exit', (code, signal) => { exited = true; exitCode = code; exitSignal = signal; });
try {
 const start = Date.now();
 while (!exited && Date.now() - start < 100000) await new Promise(resolve => setTimeout(resolve, 100));
 assert(exited, 'TTS verification did not exit'); assert.equal(exitCode, 0, 'application exit: ' + exitSignal);
 const result = JSON.parse(fs.readFileSync(script + '.result.json', 'utf8'));
 assert(result.passed, result.error);
 const native = JSON.parse(fs.readFileSync(nativeReport + '.json', 'utf8'));
 assert(native.mouthFrames > 10 && native.maxVisemes.some(value => value > 1), 'real Unity mouth did not follow playback');
 assert.equal(native.mouthOpen, 0); assert(native.currentVisemeMax < 1, 'mouth did not close after stop');
 // No local endpoint, voice paths, credentials or spoken audio in the report.
 fs.writeFileSync(path.join(output, live ? 'live-provider.json' : 'stream-fixture.json'), JSON.stringify({ ...result, native }, null, 2));
 console.log(JSON.stringify({ passed: true, source: result.source, plays: result.plays, mouthFrames: native.mouthFrames, closed: native.mouthOpen === 0 }));
} finally {
 if (!exited) child.kill('SIGKILL');
 fs.rmSync(script, { force: true }); fs.rmSync(temp, { recursive: true, force: true });
}
