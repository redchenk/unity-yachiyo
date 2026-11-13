// PCM comes from the exact decoded TTS audio which will be played. This module
// never invents motion on failure and never persists the user's speech.
export function pcm16Base64(buffer) {
  const length = Math.round(buffer.duration * 16000), bytes = new Uint8Array(length * 2);
  const view = new DataView(bytes.buffer), channels = [];
  for (let c = 0; c < buffer.numberOfChannels; c++) channels.push(buffer.getChannelData(c));
  for (let i = 0; i < length; i++) {
    const position = i * buffer.sampleRate / 16000, a = Math.floor(position), mix = position-a;
    let value = 0;
    for (const channel of channels) value += (channel[a] || 0)*(1-mix) + (channel[Math.min(a+1,channel.length-1)] || 0)*mix;
    value = Math.max(-1,Math.min(1,value/channels.length));
    view.setInt16(i*2,Math.round(value*(value<0?32768:32767)),true);
  }
  let binary = '';
  for (let i=0;i<bytes.length;i+=8192) binary += String.fromCharCode(...bytes.subarray(i,i+8192));
  return btoa(binary);
}

export async function prepareSpeechMotion(buffer, { backend='windows', cancelled=()=>false, fetchImpl=fetch, report=()=>{} }={}) {
  if (!buffer || buffer.duration < .05 || buffer.duration > 60) throw new Error('StreamTalk 支持 0.05–60 秒的单段语音');
  let id='', stopped=false, cursor=0, complete=false, sending=false;
  const chunks=[], controller=new AbortController();
  if (!['windows','ubuntu','desktop'].includes(backend)) throw new Error('未知动作服务');
  const query=`backend=${backend}`;
  const request = async (suffix, options={}) => {
    if (cancelled() || stopped) throw new DOMException('Speech motion cancelled','AbortError');
    const response = await fetchImpl(`/api/speech-motion${suffix}${suffix.includes('?')?'&':'?'}${query}`, {
      ...options, headers:{'Content-Type':'application/json'}, signal:controller.signal
    });
    const data=await response.json();
    if (!response.ok || data.status==='failed') throw new Error(data.error || `StreamTalk HTTP ${response.status}`);
    return data;
  };
  const stop = () => {
    if(stopped)return; stopped=true; controller.abort(); chunks.length=0;
    if(id)fetchImpl(`/api/speech-motion/jobs/${id}?${query}`,{method:'DELETE',headers:{'Content-Type':'application/json'}}).catch(()=>{});
  };
  const cancelTimer=setInterval(()=>{if(cancelled()){clearInterval(cancelTimer);stop();}},100);
  const poll = async () => {
    const data=await request(`/jobs/${id}?after=${cursor}`);
    if(data.motion){
      if(data.motion.startFrame!==cursor)throw new Error('StreamTalk 动作帧不连续');
      cursor+=data.motion.frames.length; chunks.push(data.motion);
    }
    complete=data.status==='done' && cursor>=data.availableFrames;
    return data;
  };
  try {
    const job=await request('/jobs',{method:'POST',body:JSON.stringify({sampleRate:16000,pcm16:pcm16Base64(buffer),seed:crypto.getRandomValues(new Uint32Array(1))[0]&0x7fffffff})});
    id=job.id;
    const deadline=Date.now()+180000;
    while(!chunks.length){
      await poll();
      if(Date.now()>deadline)throw new Error('StreamTalk 首段生成超时，请检查所选电脑的服务');
      if(!chunks.length)await new Promise(resolve=>setTimeout(resolve,100));
    }
    report({status:'ready',model:'StreamTalk',seconds:chunks[0].generationSeconds});
  } catch(error){clearInterval(cancelTimer);stop();throw error;}
  // Each packet is below the 64-KiB avatar bridge limit. A one-frame remainder
  // joins the prior packet because Unity validates at least two frames per clip.
  async function drain(driver,audio){
    while(chunks.length && !stopped){
      const motion=chunks.shift();
      for(let start=0;start<motion.frames.length;){
        const size=motion.frames.length-start===13?13:Math.min(12,motion.frames.length-start);
        if(!driver.motion(audio,{...motion,startFrame:motion.startFrame+start,frames:motion.frames.slice(start,start+size)})){
          throw new Error('Unity 动作连接已改变');
        }
        start+=size;
        await new Promise(resolve=>setTimeout(resolve,0));
      }
    }
  }
  return {
    id,
    async start(driver,audio){
      if(sending)return; sending=true;
      try{
        await drain(driver,audio);
        while(!stopped&&!complete){await poll();await drain(driver,audio);if(!complete)await new Promise(resolve=>setTimeout(resolve,80));}
      }catch(error){if(!stopped&&error.name!=='AbortError')report({status:'error',error:error.message});}
      finally{clearInterval(cancelTimer);}
    },
    stop(){clearInterval(cancelTimer);stop();}
  };
}
