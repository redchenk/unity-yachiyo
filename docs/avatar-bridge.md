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
| motion | Generated StreamTalk frames for the current utterance, contiguous startFrame, 2–13 frames per packet |
| control | Visibility, camera lock/reset/x/y/zoom, Windows/Ubuntu backend and reconnect |
| cancel | Clears queued actions, audio ownership and motion requests; advances epoch |

A/I/U/E/O map to vrc.v_aa, vrc.v_ih, vrc.v_ou, vrc.v_e, vrc.v_oh. Unity bounds and normalizes weights. JawOpen is only a fallback when vowel evidence is absent. The face controller owns final blendshape writes, including eyebrow/eyeline synchronization and automatic blinking.

The director retains audio playback; blob URLs never cross the bridge. Pause/waiting preserves the current identity and motion buffer while closing the mouth; resume reuses that identity and media clock. End/cancel drops it. Reconnect requires fresh commands. The speech player does not offer backward seeking within a neural motion utterance.

Speech with `neuralMotion: true` owns the body through StreamTalk. Mouth frames supply the actual media clock; no frame-delta clock or procedural arm/head overlay advances this motion. Pause holds. Normal `audio.end` carries `continueMotion: true` only when a non-expired sentence remains queued: Unity releases the old buffer but retains its final pose while the next sentence generates. The next utterance still needs a fresh identity. Without a queued successor, a short grace period precedes a 1.15-second return to idle; explicit cancellation starts that return immediately. A failed or missing successor cannot hold forever (12-second continuation timeout). Missing future frames within an utterance hold the last generated pose and increment an underrun counter. Local semantic gestures are disabled during neural speech. Thinking/listening never invent a head tilt. Singing does not start StreamTalk speech jobs.

New motion clips blend directly from the displayed pose and bounded outgoing joint/root velocity over 0.5 seconds. A quintic blend eases into the moving destination; queued model actions retain the outgoing pose while waiting, rather than resetting before every request. Root displacement is preserved across clips. These are runtime continuity corrections, not changes to the neural model's output or training.

Idle uses slow asymmetric weight transfer, planted-foot two-bone IK, independently timed wrist/finger drift, and variable breathing with delayed chest/shoulder/arm response. The life clock continues through actions. Eyes make small rapid attention changes before a delayed, small head-yaw response; no thinking head roll is introduced. Blinks have irregular intervals, occasional doubles, fast closure and slower reopening, with synchronized eye-line/brow shapes. These physiological idle details are local procedural animation requested separately from AI semantic actions. Cloth continues through the existing native solver with the original shoulder/sleeve binding.

The frontend decodes the exact playable audio to 16-kHz mono PCM16, submits `/api/speech-motion/jobs`, waits for the first generated window, then starts audio and polls bounded frame batches. The loopback gateway proxies only fixed Windows (18787) or Ubuntu (8787) service ports. The GPU service shares DiP's single executor, limits active audio jobs to two, validates at most 60 seconds per request, and keeps generated audio/frames in ephemeral memory. Cancellation deletes the job; stale session/epoch/utterance frames cannot animate Unity. The official 60-frame sampler retains eight overlap frames until the next window refines them. It uses the real WavLM features, coarse generation, exact chunked FP16 L1 retrieval, and keypose refinement.

Speech `audio.start` may carry `payload.performance` version 1 with intent, emotion, intensity, energy, gaze, gestureAmount and at most three cues. Each cue has type, side, intensity, startMs, strokeMs, durationMs, prepareMs and releaseMs. These times come from final audio, never from LLM-generated milliseconds. Unity consumes them against `mouth.audioTimeMs`, once per utterance, on independent head and arm channels. Late expired cues are discarded. A zero gestureAmount explicitly suppresses ambient arms; it does not remove breathing or blinking. End/cancel clears the timeline and restores a decaying affect baseline. Legacy `perform` remains for manual/model actions.

The current frontend compiles onset cues from decoded audio and validates semantic spans, but has no deployed forced aligner. Anchors without verified final-text word times are omitted with a reason. No alignment or neural gesture quality is implied by an onset cue. LLM plans are committed to session affect only when speech actually starts; cancelled prefetched plans cannot update it.

Camera x/y are normalized viewport offsets in [-0.65, 0.65], positive right/up. Zoom is an exponential distance delta. Lock rejects position/zoom changes. Capabilities publish cameraX/Y/Distance/Yaw/Pitch and cameraLocked for the controls. Plain left drag pans, right or Shift-left drag rotates; framing is camera composition, leaving avatar roots and cloth simulation unchanged. PlayerPrefs saves position, zoom and orbit after a debounce; diagnostic runs do not write these preferences.

During idle the camera follows the stable standing origin, so it does not cancel the avatar's small weight shifts or make planted feet appear to slide. Locomotion still updates the follow target, with damped transitions.

Receipts: accepted, started, completed, cancelled, unsupported, failed. Model execution starts once a real clip reaches the avatar; local preview is explicit. Gaze targets eyes, and does not imply scene understanding, grasping or object interaction.

The application starts headless.mjs with its parent PID and a private run-state path. SIGTERM or parent death closes owned memory/game services and removes the temporary token. Unity never blocks its quit callback on a request requiring its own player loop. Runtime tests check ownership, rejected shutdown requests and cleanup.

Settings, memory and audio remain local. Git contains no API keys, login sessions, weights or character packages. The optional WebKit verification hook requires an explicit CLI flag and uses a non-persistent website data store.
