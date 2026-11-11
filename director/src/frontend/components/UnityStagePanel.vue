<script setup>
import { ref } from 'vue';
const bodyMode=ref(localStorage.getItem('yachiyo:avatar:bodyMode') || 'model');
const saveBodyMode=()=>localStorage.setItem('yachiyo:avatar:bodyMode',bodyMode.value);
const props=defineProps({ ready: Boolean, connection: String, receipt: Object, capabilities: Object, compact: Boolean });
const act = type => window.TSUKUYOMI_AVATAR_DRIVER?.perform({ emotion: 'happy', behaviorActions: [{ type }], durationMs: 3500 });
const states={accepted:'已接收',started:'正在执行',completed:'已完成',cancelled:'已取消',unsupported:'暂不支持',failed:'执行失败'};
const selectBackend = event => window.TSUKUYOMI_AVATAR_DRIVER?.control('backend',event.target.value);
const reconnect = () => window.TSUKUYOMI_AVATAR_DRIVER?.control('reconnect',1);
const stop = () => window.dispatchEvent(new CustomEvent('yachiyo:avatar-stop'));
const control = (action,value=0) => window.TSUKUYOMI_AVATAR_DRIVER?.control(action,value);
const move = (axis,amount) => control('camera_'+axis,Math.max(-.65,Math.min(.65,Number(props.capabilities?.[axis==='x'?'cameraX':'cameraY']||0)+amount)));
</script>

<template>
  <aside class="unity-stage-panel" :class="{ compact }" aria-label="Unity 角色连接" aria-live="polite">
    <span class="unity-status" :class="{ connected: ready }">{{ ready ? '● 3D 角色就绪' : '○ 等待 3D 客户端' }}</span>
    <h2>八千代 · 3D 舞台</h2>
    <p>{{ connection }}</p>
    <p>角色画面与相机操作在「Yachiyo Motion Studio」窗口。这里继续控制直播、弹幕、语音和点歌。</p>
    <label style="font-size:13px">身体动作 <select v-model="bodyMode" aria-label="身体动作来源" @change="saveBodyMode"><option value="model">动作模型（沿用 3D 客户端的系统连接）</option><option value="local">本地动作预览</option></select></label>
    <label v-if="capabilities?.selectedBackend" style="font-size:12px;display:block;margin-top:8px">动作服务
      <select :value="capabilities.selectedBackend" aria-label="动作服务系统" @change="selectBackend"><option value="windows">Windows</option><option value="ubuntu">Ubuntu</option></select>
      <button type="button" @click="reconnect">重连</button>
      {{ capabilities.serviceConnected ? '已连接' : '待连接' }}
    </label>
    <div class="unity-stage-buttons">
      <button :disabled="!ready" @click="act('wave')">挥手</button>
      <button :disabled="!ready" @click="act('nod')">点头</button>
      <button :disabled="!ready" @click="act('bow')">鞠躬</button>
      <button :disabled="!ready" @click="act('explain')">讲解手势</button>
      <button :disabled="!ready" @click="act('open_palms')">摊手</button>
      <button :disabled="!ready" @click="stop">停止动作与音频</button>
    </div>
    <fieldset class="unity-framing" :disabled="!ready || capabilities?.cameraLocked">
      <legend>角色位置与大小{{ capabilities?.cameraLocked ? ' · 已锁定' : '' }}</legend>
      <p class="unity-camera-help">拖动角色移动位置 · 右键或 Shift 拖动旋转 · 滚轮缩放</p>
      <label>左右 <input aria-label="角色左右位置" type="range" min="-65" max="65" step="1" :value="Math.round((capabilities?.cameraX || 0)*100)" @input="control('camera_x',Number($event.target.value)/100)"></label>
      <label>上下 <input aria-label="角色上下位置" type="range" min="-65" max="65" step="1" :value="Math.round((capabilities?.cameraY || 0)*100)" @input="control('camera_y',Number($event.target.value)/100)"></label>
      <div class="unity-position-buttons">
        <button aria-label="角色向左移动" @click="move('x',-.04)">←</button><button aria-label="角色向右移动" @click="move('x',.04)">→</button>
        <button aria-label="角色向上移动" @click="move('y',.04)">↑</button><button aria-label="角色向下移动" @click="move('y',-.04)">↓</button>
        <button @click="control('camera_zoom',.12)">放大</button><button @click="control('camera_zoom',-.12)">缩小</button>
        <button @click="control('camera_reset')">复位</button>
      </div>
    </fieldset>
    <p v-if="receipt" class="unity-receipt">执行回执：{{ states[receipt.state] || receipt.state }}{{ receipt.reason ? ' · ' + receipt.reason : '' }}</p>
    <small v-if="capabilities">已绑定 {{ capabilities.shapeCount }} 个表情通道 · A / I / U / E / O 音频口型</small>
  </aside>
</template>

<style>
.unity-stage-panel{position:absolute;z-index:5;left:8%;right:8%;top:24%;max-width:590px;margin:auto;padding:32px;border:1px solid #c5d8d4;border-radius:22px;background:#fffcf5f2;color:#283e42;box-shadow:0 18px 70px #415e5722}
.unity-stage-panel h2{font-size:26px;margin:18px 0}.unity-stage-panel p{font-size:14px;line-height:1.8}.unity-stage-panel small{font-size:12px}
.unity-status{color:#866e54;font-size:13px}.unity-status.connected{color:#22755e}.unity-stage-buttons{display:flex;gap:8px;flex-wrap:wrap;margin:24px 0}.unity-stage-buttons button{padding:9px 16px;border:1px solid #b3cdc4;background:#edf5ee;border-radius:9px;cursor:pointer}.unity-stage-buttons button:disabled{opacity:.45;cursor:default}.unity-receipt{overflow-wrap:anywhere}
.unity-avatar-mode #live2d-container{pointer-events:none;visibility:hidden}
.unity-stage-panel.compact{position:static;padding:8px;margin:0 0 12px;max-width:none;border-radius:10px;box-shadow:none}
.unity-stage-panel.compact h2,.unity-stage-panel.compact p:not(.unity-receipt),.unity-stage-panel.compact small{display:none}
.unity-stage-panel.compact .unity-stage-buttons{margin:10px 0;gap:5px}.unity-stage-panel.compact .unity-stage-buttons button{padding:6px 9px;font-size:11px}.unity-stage-panel.compact .unity-receipt{font-size:11px;margin:4px 0}
.unity-framing{border:1px solid #d7dfda;border-radius:10px;margin:12px 0 0;padding:10px;font-size:12px}.unity-framing label{display:flex;gap:10px;align-items:center;margin:8px 0}.unity-framing input{flex:1;min-width:0;accent-color:#a64d75}.unity-position-buttons{display:flex;gap:5px;flex-wrap:wrap}.unity-position-buttons button{border:1px solid #d7ccd5;border-radius:7px;background:#fff8fc;color:#674c62;padding:6px 9px;cursor:pointer}.unity-framing:disabled{opacity:.5}.unity-stage-panel.compact p.unity-camera-help{display:block;font-size:11px;line-height:1.6;margin:0;color:#786d7b}
@media(max-width:1000px){.unity-stage-panel{padding:20px;left:3%;right:3%;top:20%}}
</style>
