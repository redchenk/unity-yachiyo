import { build } from 'vite';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
import { lipsyncFixture } from './lipsync-fixtures.mjs';
const root=path.resolve(import.meta.dirname,'../..'),project=path.dirname(root);
const output=path.join(project,'artifacts/unified-verification');fs.mkdirSync(output,{recursive:true});
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'yachiyo-unified-'));
const script=path.join(output,'browser-test.js'),nativeReport=path.join(output,'native');
const built=await build({configFile:false,logLevel:'error',build:{write:false,lib:{entry:path.join(root,'tools/desktop/avatar-browser-entry.mjs'),formats:['iife'],name:'AvatarFixture'}}});
const bundle=(Array.isArray(built)?built[0]:built).output.find(x=>x.type==='chunk').code;
const fixture=lipsyncFixture();
fs.writeFileSync(script,bundle+`\n(async()=>{
const post=data=>window.webkit.messageHandlers.yachiyoVerification.postMessage(data);
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const wait=async(fn,label)=>{for(let n=0;n<300;n++){if(fn())return;await delay(50);}throw Error(label);};
const result={done:false,passed:false};
try{
 await wait(()=>window.TSUKUYOMI_AVATAR_DRIVER?.ready,'avatar not ready');
 document.querySelector('.studio-settings-header button')?.click();
 const driver=window.TSUKUYOMI_AVATAR_DRIVER;
 const original=fetch;const bytes=Uint8Array.from(atob('${fixture.wav.toString('base64')}'),c=>c.charCodeAt(0));
 window.fetch=(url,options)=>String(url)==='/api/tts'?Promise.resolve(new Response(bytes,{headers:{'Content-Type':'audio/wav'}})):original(url,options);
 localStorage.setItem('roomTTSSettings',JSON.stringify({enabled:true,provider:'fixture',useProxy:true}));
 __avatarTools.saveLipsyncSettings({profile:${JSON.stringify(fixture.profile)}});
 const speech=__avatarTools.createLive2DSpeechPlayer();
 const parsed=__avatarTools.parseLive2DControlPayload(JSON.stringify({reply:'Hello',live2d:{emotion:'happy',behaviorActions:[{type:'wave'}],durationMs:3500}}));if(!parsed.live2d)throw Error('semantic LLM parse failed');
 await speech.enqueue('今日は楽しいですね。',{onStart:()=>__avatarTools.dispatchRoomLive2D({...parsed.live2d,bodyMode:'local'})});
 await delay(600);post({phase:'speechClosed'});await delay(700);
 const {track}=await (await fetch('/api/rvc/tracks?name=synthetic-vowels.wav',{method:'POST',headers:{'Content-Type':'application/octet-stream'},body:bytes})).json();
 const context=new AudioContext();const decoded=await context.decodeAudioData(bytes.buffer.slice(0));const timeline=await __avatarTools.buildVocalTimeline(decoded,'synthetic-vocal.wav');await context.close();
 const song=__avatarTools.createRvcSongPlayer({loadMouth:async()=>timeline,onMouth:(value,detail)=>window.dispatchEvent(new CustomEvent('tsukuyomi:live2d-mouth',{detail:{...detail,value,source:'singing'}}))});
 await song.play({...track,mouth:{version:2}});await delay(900);song.pause();await delay(600);post({phase:'songPaused',context:driver.context});await delay(700);
 await song.play();song.seek(1.4);await delay(100);post({phase:'songVowel'});await delay(250);
 song.seek(4.6);await delay(500);post({phase:'songSilence'});await delay(600);song.stop();await delay(500);
 driver.control('visibility',0);driver.control('camera_lock',1);await delay(250);driver.control('visibility',1);driver.control('camera_lock',0);driver.control('camera_reset',1);
 speech.enqueue('テスト。').catch(()=>{});await delay(800);speech.stop();await delay(600);post({phase:'cancelled'});await delay(700);
 result.passed=true;
}catch(error){result.error=String(error.stack||error);}
post({...result,done:true});
})().catch(error=>window.webkit.messageHandlers.yachiyoVerification.postMessage({done:true,passed:false,error:String(error)}));`);
const binary=process.env.YACHIYO_UNITY_BINARY||(fs.existsSync(path.join(os.homedir(),'Applications/Yachiyo Motion Studio.app'))?path.join(os.homedir(),'Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio'):path.join(project,'build/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio'));
const child=spawn(binary,['--no-auto-connect','--verify-director',nativeReport,'--verify-web-script',script,'-logFile',path.join(output,'player.log')],{stdio:'ignore',env:{...process.env,YACHIYO_DATA_DIR:temp}});
const phases={};let exited=false,exitCode;child.on('exit',code=>{exited=true;exitCode=code;});
const start=Date.now();
try{
 while(!exited&&Date.now()-start<100000){
  try{const progress=JSON.parse(fs.readFileSync(script+'.progress.json','utf8'));if(progress.phase&&!phases[progress.phase])phases[progress.phase]=JSON.parse(fs.readFileSync(nativeReport+'.json','utf8'));}catch{}
  await new Promise(r=>setTimeout(r,100));
 }
 assert(exited,'single application did not exit');assert.equal(exitCode,0);
 const result=JSON.parse(fs.readFileSync(script+'.result.json','utf8'));assert(result.passed,result.error);
 const native=JSON.parse(fs.readFileSync(nativeReport+'.json','utf8'));assert(native.maxVisemes.every(n=>n>5));assert(native.maxJawWithViseme<1);
 for(const name of ['speechClosed','songPaused','songSilence','cancelled']){assert(phases[name],name+' missing');assert.equal(phases[name].mouthOpen,0,name+' mouth remained open');assert(phases[name].currentVisemeMax<1,name+' mesh remained open');}
 assert(phases.songVowel?.mouthOpen>.1,'singing did not drive the real avatar');
 await new Promise(r=>setTimeout(r,3500));await assert.rejects(fetch('http://127.0.0.1:3289/healthz'));
 fs.writeFileSync(path.join(output,'verification.json'),JSON.stringify({passed:true,window:'original director layout with native central avatar',audio:'calibrated synthetic fixtures in macOS WKWebView',native,phases,ownedServiceExited:true},null,2));
 console.log('Unified WKWebView / Unity / audio / cancellation / process cleanup passed');
}finally{if(!exited)child.kill('SIGKILL');fs.rmSync(temp,{recursive:true,force:true});}
