import { createApp } from 'vue';
import App from './App.vue';
import { configureAssetCssVars } from '../../src/frontend/utils/assetUrl';
import { installYachiyoMusicWindowApi } from '../../src/frontend/services/room/yachiyoMusicAdapter';
import './style.css';
import './spring.css';

if (new URLSearchParams(location.search).has('unified')) document.body.classList.add('unified-director');
configureAssetCssVars();
installYachiyoMusicWindowApi();

const app = createApp(App);

app.config.errorHandler = (err, vm, info) => {
  console.error('Live2D Studio error:', err, info);
};

app.mount('#app');

// Keep the native camera and hit-test aperture aligned with the original stage.
if (new URLSearchParams(location.search).has('unified')) {
  let previous='';
  const updateNativeStage=()=>{
    const stage=document.querySelector('.live2d-stage');
    const r=stage?.getBoundingClientRect();
    const topbar=document.querySelector('.live2d-stage-topbar')?.getBoundingClientRect();
    const captions=document.querySelector('.live2d-broadcast-hud')?.getBoundingClientRect();
    const visible=!!r&&r.width>100&&r.height>100&&!document.querySelector('.studio-settings-panel');
    const top=r?Math.max(r.top+68,topbar?.bottom+8||0):0;
    const bottom=r?Math.min(r.bottom-8,captions?.top-8||r.bottom-100):0;
    const data={visible,x:r?.left+1||0,y:top,width:Math.max(0,(r?.width||0)-2),height:Math.max(0,bottom-top),stageX:r?.left||0,stageY:r?.top||0,stageWidth:r?.width||0,stageHeight:r?.height||0,viewportWidth:innerWidth,viewportHeight:innerHeight};
    const json=JSON.stringify(data);
    if(json!==previous){previous=json;window.webkit?.messageHandlers?.yachiyoLayout?.postMessage(data);}
    requestAnimationFrame(updateNativeStage);
  };
  requestAnimationFrame(updateNativeStage);
}
