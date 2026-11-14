const os = require('node:os');
const { createKeyBootstrap } = require('./netease-bootstrap.cjs');
const { getXeapiPublicKey } = require('@neteasecloudmusicapienhanced/api/util/xeapiKey');
const { generateDeviceId } = require('@neteasecloudmusicapienhanced/api/util');
const initialize = createKeyBootstrap({ cacheDir: os.tmpdir(), getKey: getXeapiPublicKey, getDeviceId: generateDeviceId });
const allowed = new Set(['login_status', 'login_qr_key', 'login_qr_create', 'login_qr_check', 'cloudsearch', 'song_url_v1']);
let api;
process.on('disconnect', () => process.exit(0));
process.on('message', async message => {
  const { id, name, params } = message || {};
  try {
    if (!allowed.has(name)) throw new Error('Unknown music operation');
    const key = await initialize(); global.deviceId = key.deviceId;
    api ||= require('@neteasecloudmusicapienhanced/api');
    const result = await api[name](params);
    if (process.connected) process.send({ id, result });
  } catch (error) {
    // Never serialize Axios errors: they can contain account cookies and headers.
    if (process.connected) process.send({ id, error: error.message || error.body?.message || '网易云请求失败，请稍后重试。' });
  }
});
