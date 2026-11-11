// Real WKWebView audio -> bridge -> rendered Unity bones and viewport controls.
import { build } from 'vite';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
import { wav, vowelSamples } from './lipsync-fixtures.mjs';
const root = path.resolve(import.meta.dirname, '../..'), project = path.dirname(root);
const output = path.join(project, 'artifacts/conversation-verification'); fs.mkdirSync(output, { recursive: true });
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'yachiyo-conversation-'));
const script = path.join(output, 'browser-test.js'), nativeReport = path.join(output, 'native');
const built = await build({ configFile: false, logLevel: 'error', build: { write: false, lib: { entry: path.join(root, 'tools/desktop/avatar-browser-entry.mjs'), formats: ['iife'], name: 'AvatarFixture' } } });
const bundle = (Array.isArray(built) ? built[0] : built).output.find(x => x.type === 'chunk').code;
const bytes = wav(vowelSamples(0, 1.3));
fs.writeFileSync(script, bundle + `\n(async()=>{
const post=data=>window.webkit.messageHandlers.yachiyoVerification.postMessage(JSON.parse(JSON.stringify(data)));
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const wait=async(fn,label)=>{for(let i=0;i<300;i++){if(fn())return;await delay(30);}throw Error(label);};
const result={done:false,passed:false,receipts:[]};
try{
 await wait(()=>window.TSUKUYOMI_AVATAR_DRIVER?.ready,'avatar not ready');
 document.querySelector('.studio-settings-header button')?.click();
 const driver=window.TSUKUYOMI_AVATAR_DRIVER;
 const prompt=__avatarTools.live2DStreamingControlSystemPrompt();
 if(!prompt.includes('- explain:')||!prompt.includes('- open_palms:')||prompt.includes('bounce, lean_in, wave, wink'))throw Error('streaming prompt does not match Unity capabilities');
 window.addEventListener('yachiyo:avatar-receipt',e=>result.receipts.push(e.detail));
 const original=fetch, bytes=Uint8Array.from(atob('${bytes.toString('base64')}'),c=>c.charCodeAt(0));
 window.fetch=(url,options)=>String(url)==='/api/tts'?Promise.resolve(new Response(bytes,{headers:{'Content-Type':'audio/wav'}})):original(url,options);
 localStorage.setItem('roomTTSSettings',JSON.stringify({enabled:true,provider:'fixture',useProxy:true}));
 const speech=__avatarTools.createLive2DSpeechPlayer();
 driver.control('camera_lock',0);driver.control('camera_reset');await delay(600);post({phase:'idle'});await delay(400);
 let started=false;
 const play=speech.enqueue('こんにちは。',{performance:__avatarTools.affectRuntime.plan({intent:'explain'},'こんにちは。'),onStart:()=>{started=true;}});
 await wait(()=>started,'speech not started');await delay(420);post({phase:'shortSpeech'});await delay(350);
 await play;await delay(650);post({phase:'speechEnded'});await delay(400);
 const parsed=__avatarTools.parseLive2DControlPayload(JSON.stringify({reply:'これは説明です。',actions:[{type:'explain',side:'left',duration:1.5}],emotion:'happy'}));
 __avatarTools.dispatchRoomLive2D({...parsed.live2d,durationMs:1500,bodyMode:'local'});
 await delay(600);post({phase:'explain'});await wait(()=>result.receipts.some(r=>r.state==='completed'),'parsed explanation did not complete');
 driver.perform({behaviorActions:[{type:'open_palms'}],durationMs:1500,bodyMode:'local'});
 await delay(600);post({phase:'openPalms'});await wait(()=>result.receipts.filter(r=>r.state==='completed').length>=2,'open palms did not complete');
 speech.enqueue('もう一度。').catch(()=>{});await delay(500);speech.stop();await delay(600);post({phase:'cancelled'});await delay(400);
 const role=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='角色');role?.click();
 await wait(()=>document.querySelector('[aria-label="角色向右移动"]'),'camera panel missing');
 document.querySelector('[aria-label="角色向右移动"]').click();await delay(450);post({phase:'right'});await delay(350);
 document.querySelector('[aria-label="角色向上移动"]').click();await delay(450);post({phase:'up'});await delay(350);
 const range=document.querySelector('[aria-label="角色左右位置"]');range.value='-12';range.dispatchEvent(new Event('input',{bubbles:true}));
 await delay(450);post({phase:'slider'});await delay(350);
 driver.control('camera_lock',1);await delay(250);
 if(!document.querySelector('.unity-framing').disabled)throw Error('camera controls not locked');
 driver.control('camera_x',.4);driver.control('camera_y',.4);await delay(350);post({phase:'locked'});await delay(350);
 driver.control('camera_lock',0);driver.control('camera_reset');await delay(450);post({phase:'reset'});await delay(350);
 [...document.querySelectorAll('.unity-position-buttons button')].find(b=>b.textContent==='放大').click();await delay(450);post({phase:'zoom'});await delay(350);
 driver.control('camera_reset');
 result.passed=true;
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
 const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
 assert(phases.shortSpeech.speechBodyWeight > .8, 'short speech did not start body motion');
 const armMovement = Math.max(distance(phases.idle.leftHand, phases.shortSpeech.leftHand), distance(phases.idle.rightHand, phases.shortSpeech.rightHand));
 assert(armMovement > .025, 'speech arms did not move at least 2.5 cm');
 for (const phase of ['speechEnded', 'cancelled']) { assert.equal(phases[phase].speechBodyWeight, 0); assert.equal(phases[phase].mouthOpen, 0); }
 assert.equal(phases.explain.gesture, 'explain'); assert(phases.explain.localActionActive);
 assert.equal(phases.openPalms.gesture, 'open_palms'); assert(phases.openPalms.localActionActive);
 assert(result.receipts.filter(r => r.state === 'completed').length >= 2, 'semantic actions did not complete');
 assert(Math.abs(phases.right.cameraX - .04) < .001); assert(phases.right.screenHead.x > phases.idle.screenHead.x + .02);
 assert(Math.abs(phases.up.cameraY - .04) < .001); assert(phases.up.screenHead.y > phases.right.screenHead.y + .02);
 assert(Math.abs(phases.slider.cameraX + .12) < .001); assert(phases.slider.screenHead.x < phases.up.screenHead.x - .1);
 assert.equal(phases.locked.cameraX, phases.slider.cameraX); assert.equal(phases.locked.cameraY, phases.slider.cameraY);
 assert.equal(phases.reset.cameraX, 0); assert.equal(phases.reset.cameraY, 0); assert(phases.zoom.cameraDistance < phases.reset.cameraDistance);
 fs.writeFileSync(path.join(output, 'verification.json'), JSON.stringify({ passed: true, source: '1.3-second actual WKWebView audio and native Unity bones', armMovementMeters: armMovement, phases }, null, 2));
 console.log(JSON.stringify({ passed: true, armMovementMeters: armMovement, camera: 'buttons, slider, lock, reset, zoom passed' }));
} finally { if (!exited) child.kill('SIGKILL'); fs.rmSync(temp, { recursive: true, force: true }); }
