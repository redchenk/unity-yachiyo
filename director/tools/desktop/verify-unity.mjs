import { app, BrowserWindow } from 'electron';
import { build } from 'vite';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { startDesktopServer } from './server.mjs';
import { lipsyncFixture } from './lipsync-fixtures.mjs';

const root=path.resolve(import.meta.dirname,'../..'), project=path.dirname(root);
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'yachiyo-unity-'));
const output=path.join(project,'artifacts/director-integration');fs.mkdirSync(output,{recursive:true});
const binary=process.env.YACHIYO_UNITY_BINARY || (fs.existsSync(path.join(os.homedir(),'Applications/Yachiyo Motion Studio.app'))?path.join(os.homedir(),'Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio'):path.join(project,'build/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio'));
app.setPath('userData',path.join(temp,'profile'));
app.commandLine.appendSwitch('autoplay-policy','no-user-gesture-required');
app.on('window-all-closed',()=>{});
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const fixture=lipsyncFixture();let native,win,runtime;
app.whenReady().then(async()=>{
 const summary={passed:false,boundary:'macOS Electron + real Unity avatar; calibrated synthetic audio, no external LLM/TTS call or audience messages'};
 const audioWire=[];summary.audioWire=audioWire;
 try{
  runtime=await startDesktopServer({resourceRoot:root,dataDir:temp,services:{},port:0,avatarObserver:m=>audioWire.push(m),fetchImpl:async()=>new Response(fixture.wav,{headers:{'Content-Type':'audio/wav'}})});
  const launch=name=>{native=spawn(binary,['--no-auto-connect','--service-url',process.env.YACHIYO_VERIFY_SERVICE || 'http://127.0.0.1:18787','--director-url',runtime.origin.replace('http:','ws:')+'/avatar?role=unity','--verify-director',path.join(output,name),'-logFile',path.join(output,name+'.log')],{stdio:'ignore'});native.on('error',e=>console.error(e.message));};
  launch('native');
  win=new BrowserWindow({width:1440,height:960,show:true,webPreferences:{sandbox:true,contextIsolation:true,nodeIntegration:false,backgroundThrottling:false}});
  win.webContents.setAudioMuted(true);
  win.webContents.session.webRequest.onBeforeRequest((request,cb)=>cb({cancel:/^https?:/.test(request.url)&&new URL(request.url).origin!==runtime.origin}));
  const errors=[];win.webContents.on('console-message',e=>{if(e.level==='error')errors.push(e.message);});
  const js=code=>win.webContents.executeJavaScript(`{${code}\n}`,true);
  const wait=async(check,label,timeout=15000)=>{const end=Date.now()+timeout;while(Date.now()<end){if(await check())return;await delay(80);}throw new Error(label);};
  const report=async(name='native')=>{try{return JSON.parse(fs.readFileSync(path.join(output,name+'.json'),'utf8'));}catch{return {};}};
  await win.loadURL(runtime.origin+'/live2d-studio/');
  await js(`localStorage.setItem('roomTTSSettings',JSON.stringify({enabled:true,provider:'fixture',useProxy:true}));localStorage.setItem('roomMemorySettings',JSON.stringify({enabled:false}));`);
  await win.loadURL(runtime.origin+'/live2d-studio/');
  await wait(()=>js('!!window.TSUKUYOMI_AVATAR_DRIVER?.ready'),'Unity did not become ready');
  const built=await build({configFile:false,logLevel:'error',build:{write:false,lib:{entry:path.join(root,'tools/desktop/avatar-browser-entry.mjs'),formats:['iife'],name:'AvatarFixture'}}});
  await js((Array.isArray(built)?built[0]:built).output.find(x=>x.type==='chunk').code);
  await js(`window.__audioTrace=[];for(const name of ['audioStart','audioEnd','cancel']){const original=TSUKUYOMI_AVATAR_DRIVER[name];TSUKUYOMI_AVATAR_DRIVER[name]=function(...args){__audioTrace.push({name,event:typeof args[1]==='string'?args[1]:args[1]?.source,context:this.context,time:Date.now()});return original.apply(this,args);};}true;`);
  await js(`__avatarTools.saveLipsyncSettings({profile:${JSON.stringify(fixture.profile)}});window.__done=false;window.__speech=__avatarTools.createLive2DSpeechPlayer();__speech.enqueue('今日は楽しいですね。',{turnId:'integration-speech',onStart:()=>__avatarTools.dispatchRoomLive2D({emotion:'happy',bodyMode:'local',behaviorActions:[{type:'wave'}],durationMs:3500})}).then(()=>__done=true);`);
  await wait(async()=>{const r=await report();return r.localActionSeen&&r.happySeen&&r.mouthFrames>10;},'real body/face/audio did not execute');
  fs.writeFileSync(path.join(output,'director.png'),(await win.webContents.capturePage()).toPNG());
  await wait(()=>js('__done'),'speech did not finish');await delay(500);
  summary.speech=await report();
  assert(summary.speech.maxVisemes.every(v=>v>5),'all five actual mesh visemes must move');
  assert(summary.speech.maxJawWithViseme<1,'JawOpen must not double-open a vowel');
  assert(!summary.speech.audioActive&&summary.speech.closedVisemeMax<1,'end must close the real mesh');
  // Stop while speech is active. Late analyser frames and a delayed semantic step must not execute.
  await js(`__speech.enqueue('もう一度。').catch(()=>{});__avatarTools.dispatchRoomLive2D({sequence:[{emotion:'happy',delayMs:1800}]});`);
  await wait(async()=>(await report()).audioActive,'second audio did not start');
  await js(`__speech.stop();`);await delay(2200);summary.cancel=await report();
  assert(!summary.cancel.audioActive&&!summary.cancel.localActionActive&&summary.cancel.closedVisemeMax<1);
  assert.equal(summary.cancel.performances,summary.speech.performances,'cancelled delayed action executed');
  // Native process reconnect: no action/audio replay until a new playback starts.
  native.kill('SIGTERM');await wait(()=>js('!window.TSUKUYOMI_AVATAR_DRIVER.ready'),'disconnect not detected');
  launch('reconnected');await wait(()=>js('window.TSUKUYOMI_AVATAR_DRIVER.ready'),'reconnect did not become ready');await delay(500);
  summary.reconnect=await report('reconnected');assert.equal(summary.reconnect.performances,0);assert.equal(summary.reconnect.mouthFrames,0);
  await js(`__avatarTools.dispatchRoomLive2D({behaviorActions:[{type:'not_supported'}]});`);
  await wait(()=>js(`window.TSUKUYOMI_AVATAR_DRIVER.lastReceipt?.state==='unsupported'`),'unsupported action was not reported');
  summary.unsupported=true;
  // Use the production RVC player, real media clock and cached pure-vocal analysis.
  const upload=await fetch(runtime.origin+'/api/rvc/tracks?name=synthetic-vowels.wav',{method:'POST',headers:{'Content-Type':'application/octet-stream'},body:fixture.wav});
  const {track}=await upload.json();
  await js(`window.__songState={};window.__song=__avatarTools.createRvcSongPlayer({onState:s=>__songState=s,onMouth:(value,detail)=>window.dispatchEvent(new CustomEvent('tsukuyomi:live2d-mouth',{detail:{...detail,value,source:'singing'}})),loadMouth:async()=>{const c=new AudioContext();const buffer=await c.decodeAudioData(Uint8Array.from(atob('${fixture.wav.toString('base64')}'),c=>c.charCodeAt(0)).buffer);const timeline=await __avatarTools.buildVocalTimeline(buffer,'synthetic-vocal.wav');await c.close();return timeline;}});__song.play(${JSON.stringify({...track,mouth:{version:2}})});`);
  await wait(async()=>(await report('reconnected')).mouthFrames>12,'singing did not reach Unity');
  await js(`__song.pause()`);await wait(async()=>{const r=await report('reconnected');return r.audioActive===false&&r.closedVisemeMax<1;},'pause did not close native mouth');summary.singingPaused=await report('reconnected');assert(!summary.singingPaused.audioActive&&summary.singingPaused.closedVisemeMax<1);
  await js(`__song.play();`);await wait(()=>js(`__songState.status==='playing'`),'song did not resume');
  await js(`__song.seek(.65)`);await wait(async()=>(await report('reconnected')).mouthOpen>.1,'backward seek did not create a fresh mouth clock');
  await js(`__song.seek(4.6)`);await delay(400);summary.singingSeekSilence=await report('reconnected');assert(summary.singingSeekSilence.mouthOpen===0&&summary.singingSeekSilence.closedVisemeMax<1);
  await js(`__song.stop()`);await delay(350);summary.singingStopped=await report('reconnected');assert(!summary.singingStopped.audioActive);
  if(process.env.YACHIYO_VERIFY_MODEL==='1'){
   await js(`__avatarTools.dispatchRoomLive2D({bodyMode:'model',behaviorActions:[{type:'wave'}],durationMs:2500});`);
   await wait(async()=>(await report('reconnected')).generatedMotionSeen,'GPU motion did not reach avatar',55000);
   summary.model=await report('reconnected');assert(summary.model.motionSource.includes('DiP'));
   await wait(()=>js(`window.TSUKUYOMI_AVATAR_DRIVER.lastReceipt?.state==='completed'`),'model performance did not complete',20000);
  }
  summary.errors=errors;summary.passed=true;
  console.log(JSON.stringify(summary,null,2));
 }catch(error){summary.error=error.stack;console.error(error);process.exitCode=1;if(win){summary.audioTrace=await win.webContents.executeJavaScript('window.__audioTrace');fs.writeFileSync(path.join(output,'failure.png'),(await win.webContents.capturePage()).toPNG());}}
 finally{fs.writeFileSync(path.join(output,'verification.json'),JSON.stringify(summary,null,2));native?.kill('SIGTERM');win?.destroy();await runtime?.close();await fs.promises.rm(temp,{recursive:true,force:true});app.exit(process.exitCode||0);}
});
