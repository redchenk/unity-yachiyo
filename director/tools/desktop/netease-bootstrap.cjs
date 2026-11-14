const fs = require('node:fs');
const path = require('node:path');

function validKey(value) {
  return value && typeof value.publicKey === 'string' && Buffer.from(value.publicKey, 'base64').length === 32 &&
    typeof value.sk === 'string' && value.sk.length > 0 && value.version != null;
}

function createKeyBootstrap({ cacheDir, getKey, getDeviceId }) {
  const file = path.join(cacheDir, 'xeapi_public_key');
  let initialized, ready;
  return () => {
    if (ready) return Promise.resolve(ready);
    if (!initialized) initialized = (async () => {
      try {
        const cached = JSON.parse(fs.readFileSync(file, 'utf8'));
        if (validKey(cached)) { ready = cached; return cached; }
      } catch {}
      const deviceId = getDeviceId();
      let key;
      try { key = await getKey({}, deviceId); } catch { throw new Error('网易云连接初始化失败，请检查网络后重试。'); }
      if (!validKey(key)) throw new Error('网易云返回的连接公钥无效，请稍后重试。');
      fs.mkdirSync(cacheDir, { recursive: true, mode: 0o700 });
      const temporary = file + '.partial';
      fs.writeFileSync(temporary, JSON.stringify({ ...key, deviceId }), { mode: 0o600 });
      fs.renameSync(temporary, file);
      ready = { ...key, deviceId }; return ready;
    })().finally(() => { initialized = null; });
    return initialized;
  };
}
module.exports = { createKeyBootstrap, validKey };
