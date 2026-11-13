import test from 'node:test';
import assert from 'node:assert/strict';
import { pcm16Base64, prepareSpeechMotion } from '../../../src/frontend/services/room/speechMotion.mjs';

const buffer={duration:.1,sampleRate:16000,numberOfChannels:1,getChannelData:()=>new Float32Array(1600).fill(.25)};
const response=data=>({ok:true,json:async()=>data});

test('speech PCM preserves signed levels and downmixes stereo',()=>{
  const encoded=pcm16Base64({...buffer,duration:4/16000,numberOfChannels:2,getChannelData:()=>new Float32Array([-1,0,.5,1])});
  const bytes=Buffer.from(encoded,'base64');
  assert.deepEqual([0,2,4,6].map(i=>bytes.readInt16LE(i)),[-32768,0,16384,32767]);
});

for (const backend of ['windows','ubuntu','desktop']) test(`${backend}: ordered frames and cancellation stay on the selected host`,async()=>{
  const calls=[],sent=[];
  const fetchImpl=async(url,options)=>{
    calls.push([url,options]);
    if(options.method==='POST')return response({id:'abc',status:'queued'});
    if(options.method==='DELETE')return response({cancelled:true});
    return response({status:'done',availableFrames:53,motion:{startFrame:0,generationSeconds:.5,frames:Array.from({length:53},(_,i)=>i)}});
  };
  const job=await prepareSpeechMotion(buffer,{fetchImpl,backend});
  await job.start({motion:(audio,clip)=>{sent.push(clip);return true;}},{});
  assert.deepEqual(sent.flatMap(x=>x.frames),Array.from({length:53},(_,i)=>i));
  assert(sent.every(x=>x.frames.length>=2&&x.frames.length<=13));
  assert.deepEqual(sent.map(x=>x.startFrame),[0,12,24,36,48]);
  job.stop();assert(calls.every(([url])=>url.includes(`backend=${backend}`)));
  assert(calls.some(([,o])=>o.method==='DELETE'));
});

test('unknown speech backend fails before submitting audio',async()=>{
  await assert.rejects(prepareSpeechMotion(buffer,{backend:'foreign',fetchImpl:()=>assert.fail('Unexpected request')}),/未知动作服务/);
});

test('cancel during warmup rejects and deletes job without sending old frames',async()=>{
  let cancelled=false,deleted=false;
  const job=prepareSpeechMotion(buffer,{cancelled:()=>cancelled,fetchImpl:async(url,o)=>{
    if(o.method==='POST')return response({id:'abc'});
    if(o.method==='DELETE'){deleted=true;return response({});}
    cancelled=true;return response({status:'running',availableFrames:0});
  }});
  await assert.rejects(job,{name:'AbortError'});assert(deleted);
});

test('model failure is surfaced and no replacement motion is fabricated',async()=>{
  await assert.rejects(prepareSpeechMotion(buffer,{fetchImpl:async(url,o)=>o.method==='POST'?response({id:'abc'}):response({status:'failed',error:'CUDA out of memory'})}),/CUDA out of memory/);
});
