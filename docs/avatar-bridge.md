# Avatar bridge v1

The unified macOS player owns a headless Node service and embeds the original director UI using WKWebView. A layout message aligns the native camera aperture with the original character canvas; mouse events in that aperture reach Unity. Navigation, controls, captions and theme remain HTML. No second GUI or external browser is needed.

Node binds loopback. WebSocket `/avatar?role=unity` has one avatar owner; `/avatar?role=director` has one director owner. Foreign origins/hosts, oversized frames, flooding and duplicate owners are rejected. This is a local application protocol, not a public or LAN service.

Every command carries sessionId, epoch, seq, commandId, turnId, utteranceId, kind and payload. The gateway assigns the session and advances epoch on cancel/disconnect. The client invalidates delayed callbacks immediately; Unity invalidates pending model-service requests through its request version. No persistent replay queue exists.

| Kind | Purpose |
| --- | --- |
| perform | Emotion and whitelisted actions, at most one full-body model action per performance |
| state | Idle/listening/thinking/speaking; no fake phonemes |
| audio | Actual start, pause, end/cancel with an utterance identity |
| mouth | Latest opening, silence, A/I/U/E/O and media clock; expires after 250 ms |
| control | Visibility, camera lock/reset, Windows/Ubuntu backend and reconnect |
| cancel | Clears queued actions, audio ownership and motion requests; advances epoch |

A/I/U/E/O map to vrc.v_aa, vrc.v_ih, vrc.v_ou, vrc.v_e, vrc.v_oh. Unity bounds and normalizes weights. JawOpen is only a fallback when vowel evidence is absent. The face controller owns final blendshape writes, including eyebrow/eyeline synchronization and automatic blinking.

The director retains audio playback; blob URLs never cross the bridge. Pause/waiting/seek closes the old identity; resume creates a new identity so backward seek is valid while stale frames are rejected. Reconnect requires fresh commands or playback.

Receipts: accepted, started, completed, cancelled, unsupported, failed. Model execution starts once a real clip reaches the avatar; local preview is explicit. Gaze targets eyes, and does not imply scene understanding, grasping or object interaction.

The application starts headless.mjs with its parent PID and a private run-state path. SIGTERM or parent death closes owned memory/game services and removes the temporary token. Unity never blocks its quit callback on a request requiring its own player loop. Runtime tests check ownership, rejected shutdown requests and cleanup.

Settings, memory and audio remain local. Git contains no API keys, login sessions, weights or character packages. The optional WebKit verification hook requires an explicit CLI flag and uses a non-persistent website data store.
