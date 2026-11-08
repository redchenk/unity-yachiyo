// Test-only bundle used by verify-unity.mjs inside the sandboxed renderer.
import { createLive2DSpeechPlayer } from '../../src/frontend/services/room/live2dSpeech.js';
import { saveLipsyncSettings } from '../../src/frontend/services/room/lipsyncSettings.js';
import { dispatchRoomLive2D } from '../../src/frontend/services/room/live2dControl.js';
import { createRvcSongPlayer } from '../../src/frontend/services/room/rvcSongPlayer.js';
import { buildVocalTimeline } from '../../src/frontend/services/room/rvcVocalTimeline.js';
import { parseLive2DControlPayload } from '../../src/frontend/services/room/live2dLlmControl.js';
window.__avatarTools = { createLive2DSpeechPlayer, saveLipsyncSettings, dispatchRoomLive2D, createRvcSongPlayer, buildVocalTimeline, parseLive2DControlPayload };
