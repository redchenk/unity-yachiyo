# Validation — 2026-11-08

- Unity 6000.6.4f1 macOS build completed. The unified Apple Silicon application was locally signed and passed deep/strict signature verification.
- The original director UI was observed in the same native window, with the Unity avatar in its central stage and the original navigation, controls and captions.
- The actual WKWebView → local gateway → Unity test passed: parsed semantic LLM fixture, live audio start, all five real mesh visemes, no added JawOpen on vowels, singing pause/resume/backward seek/silence, explicit cancel and automatic owned-service shutdown.
- Peak A/I/U/E/O shape weights in the final native fixture run: 39.51, 42.75, 40.18, 41.80, 40.00. Closed phases returned to zero; the test uses synthetic calibrated audio, not a live voice provider.
- Windows laptop service reported DiP 10-step / DistilBERT / RTX 3070. A real generated wave reached the native avatar and returned accepted → started → completed. Ubuntu remains deferred at the user's request.
- Director unit/integration tests: 35 passed, 1 skipped (external NSIS compiler fixture). Motion service logic tests: 17 passed.
- In the final signed application, native camera dragging visibly rotated the central 3D avatar; reset restored the front view. Hide/show removed and restored the character while preserving the original garment renderer states.
- No real audience messages or paid LLM requests were sent. Human listening and microphone permission remain separate from the automated audio checks.

## GPT-SoVITS playback correction

- Captured the configured provider's actual 32 kHz PCM WAV response. It contained 146,560 bytes of PCM but advertised RIFF size 36 and data size 0. The original WKWebView speech path reproduced `Audio playback failed`; the same PCM played after correcting only the length fields.
- The Mac unified player now downloads each generated line once, completes unknown WAV lengths and plays a local Blob. This avoids WebKit probing a single-use relay URL. The tradeoff is waiting for that line's synthesis to finish before playback; the next line can still be prefetched.
- Stop aborts pending audio requests, including response-body downloads. Malformed/empty audio is rejected, complete WAV and other audio formats are retained, and cancellation is distinguished from timeout.
- The actual installed page was tested via its Start Live button, without injecting a replacement speech player. A fragmented zero-length WAV fixture played all 5 seconds, drove the real five Unity visemes and closed the mouth after stop.
- The same installed-page test passed against the user's configured GPT-SoVITS endpoint: 2.41 seconds of real generated speech played to completion; 149 mouth frames reached Unity and the mouth returned to zero. LLM output was a local Japanese fixture; no paid LLM request was made. This is playback/mesh evidence, not a claim of human listening acceptance.
- Regression coverage includes WAV header completion with padded metadata, truncated/empty audio, unchanged MP3 bytes, single-use URL consumption, aborting downloads and playing the next line after cancellation. The native diagnostic bridge also rejects non-JSON numbers instead of throwing during verification.

Detailed runtime reports and screenshots stay in local artifacts/ and are not published with character assets or private settings.

## Conversational gestures and framing — 2026-11-11

- Unity build, frontend build and deep/strict Mac app signature verification passed. Director tests: 37 passed, 1 skipped (external NSIS fixture).
- A 1.3-second synthetic utterance played through actual WKWebView audio and moved a rendered Unity hand by 11.7 cm from the idle sample. The body layer reached full weight during the short sentence, then returned to zero after playback and explicit stop. This measures bone response, not subjective naturalness or a live LLM's semantic choices.
- Parsed explanation and open-palms actions produced native gestures and completed receipts. The streaming prompt advertises connected Unity gestures; Live2D-only emotion fallback actions are replaced with supported conversational actions.
- The installed page's Start Live path consumed a streaming BEAT containing an explanation gesture, played the full five-second zero-length-header WAV fixture, drove 285 mouth frames and the speech body layer, and closed both after stop. No replacement speech player was injected in this check.
- Native viewport tests passed for right/up buttons, the position slider, lock rejection, reset and zoom. Actual mouse dragging moved the avatar right/up, and left/down buttons moved it back with matching slider values. A normal quit/relaunch visibly restored the saved x/y offsets; reset then returned the avatar to center. Explanation and two-arm gesture previews were visually inspected in the installed application.
- Existing unified audio regression passed for five visemes, singing pause/resume/seek/silence, cancellation and owned-service shutdown. No new paid LLM calls or audience messages were sent; Windows/Ubuntu motion generation was not changed by this update.

Reproduce native conversation/framing coverage with `node director/tools/desktop/verify-conversation.mjs`; installed-page streaming coverage uses `node director/tools/desktop/verify-tts.mjs`. Both require the local licensed model and a built Mac application, and keep reports under ignored `artifacts/`.

## Persistent affect and performance planning — 2026-11-11

- Frontend build and Unity macOS build passed; the unified app was installed and passed deep/strict signing checks. Director tests: 45 passed, 1 skipped (external NSIS compiler).
- Seven actual WKWebView audio → native Unity scenarios passed: gratitude, reassurance, explanation, teasing, surprise, restrained refusal and quiet listening. Face preset and gaze intent reached Unity; the reduced expression survived utterance end. Six expressive plans fired bounded onset cues; quiet listening fired none and had zero ambient arm gain.
- Native hand displacement was 11.6 cm in the explanation sample and 0.47 cm in quiet listening. Utterance end and cancellation returned speech weight and mouth opening to zero. This measures native execution, not human-like quality.
- The installed page test interrupted a playing sentence with a local text message. The old Audio element was paused and released, its queued second sentence never played, and the next generation received listener-distress context. The new plan reached Unity as `ConcernedWarm` with no arm gestures.
- Continuous-recorder tests verified opt-in gating after requested browser echo cancellation, retained voice onset samples, old-recognition invalidation, fresh recognition and microphone track cleanup. No physical microphone or loudspeaker echo test was performed; voice interruption remains off by default.
- The installed page played the complete five-second fragmented unknown-length WAV fixture, drove 287 mouth frames and closed the real mouth after stop. Unified regression passed for five visemes, singing pause/resume/seek/silence, cancellation and owned-service shutdown. Camera button/slider/lock/reset/zoom regression also passed.
- The configured real GPT-SoVITS endpoint returned HTTP 503, and the prior laptop address was unreachable. The user chose to continue available validation. Real-provider voice quality, live LLM choices and new Windows/Ubuntu generation were not accepted in this run. No paid LLM request or real audience message was sent.
- Exact word alignment is intentionally not claimed: current cues use decoded audio onset and playback time. Unavailable/ambiguous/changed-text semantic anchors are skipped; there is no deployed forced aligner. GPT-SoVITS receives bounded speed, with the reference audio/transcript preserved; emotion-specific reference-voice calibration remains separate.

Reproduce with `node director/tools/desktop/verify-performance.mjs` and `node director/tools/desktop/verify-interruption.mjs`. Both require the built private Mac application and keep all runtime evidence under ignored `artifacts/performance-validation/`.

## Explicit AI control without thinking poses — 2026-11-13

- Removed the Unity thinking/listening pose overlay, automatic speech arm/head loop and mouse-driven head tracking in the unified app. `head_tilt` is absent from AI prompts/capabilities and rejected at the native bridge. Empty/missing/malformed gestures no longer produce an emotion-based fallback; legacy keyword and expression presets cannot add Unity body actions.
- Frontend and Unity builds passed; the installed Mac application passed deep/strict signature verification. Director tests: 46 passed, 1 skipped (external NSIS compiler fixture).
- Actual WKWebView → native Unity checks passed for thinking, listening, rejected legacy tilt and speech without gesture instructions. Head/neck local rotation stayed within 0.1 degrees of idle. Seven explicit performance scenarios retained their intended face and allowed gestures; stopping speech closed the mouth and cancelled cues. The explanation hand displacement was 11.84 cm; quiet speech displacement was 0.19 cm from idle breathing.
- Short-utterance and native camera button/slider/lock/reset/zoom checks passed. The actual page interruption test dropped old queued speech and forwarded the new context. The full five-second WAV fixture drove 282 mouth frames and closed correctly. These are deterministic LLM-plan/audio fixtures; they do not establish a live LLM's choices or real-provider voice quality.
- Windows SSH and existing DiP health were checked successfully on the RTX 3070 Laptop GPU (8 GB). No new neural speech-motion model was installed: StreamTalk weights, retrieval data and separately licensed SMPL-X assets are not present in the existing motion installation. Autonomous AI decision-making and local gesture execution must not be described as deployed audio-conditioned neural generation.

## Rendering and neural motion cadence — 2026-11-13

- The current Windows service now contains the real StreamTalk integration described in the README; the preceding section records the earlier state before installation. This performance change does not change the model or its 30 FPS output rate.
- Same M5 Mac, installed release app, focused 2940×1584 window, unchanged saved camera and quality, 8-second warmup followed by 20-second capture: baseline 59.999 FPS, optimized 59.998 FPS. Cloth transfer mean fell from 3.211 ms to 2.091 ms (35%); P95 from 3.763 ms to 2.386 ms. The first instrumented run included an external process sampler and was excluded from the comparison. This establishes lower CPU cost, not an increase beyond the display's 60 Hz cap. GPU/GC recorder values were unavailable (zero) and must not be interpreted as zero cost.
- Removed six full 11,717-vertex CPU mesh bakes per frame, preserving all original bind poses, weights, shoulder topology, collision settings and solver frequencies. Reused hem render/shell/correction arrays. All six native cloth panels and 1,104 simulation vertices remain enabled.
- Native preserved-hem tests passed for standing, wave, return, turn, model playback, stop and settling, plus cloth disable/re-enable. Maximum seam-skinning deviation from Unity BakeMesh was 4.80e-7 metres; visible seam gap was zero in recorded samples. Front/back renders were inspected. These checks protect the optimization's equivalence; they do not establish that every possible generated pose is collision-free.
- Audio clock messages previously held body poses between reports. Rendering now advances between reports using bounded media-time prediction and interpolates the generated poses; it cannot predict beyond 50 ms without a fresh clock report. Pause uses the reported media position, and cancellation releases the body. Actual Windows GPU inference → WKWebView WAV playback → native Unity tests passed, including render-cadence interpolation, no sampled buffer underruns, pause, resume, completion and cancellation. Cadence is sampled after audio-device startup and the diagnostic PNG capture; audio stalls intentionally hold motion. Voice is a real prerecorded WAV replacing only TTS, not a successful live TTS provider request.
- Unity release build and installed deep/strict code signature passed. Python tests: 19 passed. Director tests: 50 passed, 1 external NSIS fixture skipped. Local evidence: `artifacts/render-performance/` and `artifacts/streamtalk-native/result.json` (ignored; no private character/model data committed).

Reproduce the lightweight release performance capture (writes JSON/PNG, then exits):

```sh
"$HOME/Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio" \
  --no-auto-connect --profile-render /absolute/output/prefix
```

`--profile-action sample` also plays the bundled motion during the capture. Native geometry equivalence: `--verify-preserved-hems /absolute/output/prefix`. Playback regression: `node director/tools/desktop/verify-streamtalk.mjs /absolute/path/spoken.wav`. Profiling is opt-in and does not record in normal use. Timing counters follow [Unity's release-player ProfilerRecorder guidance](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/graphics-performance-profiling/profile-rendering/frame-timing-manager/record-timing-data).

## Natural idle and direct action transitions — 2026-11-13

- Replaced the shared periodic idle with asymmetric weight transfer, planted feet, variable breath duration, delayed torso-to-arm response, independent wrist/finger drift, irregular fast-close/slow-open blinks and eye-led attention. These are local physiological details; semantic actions still require AI instructions or generated motion. No thinking head-tilt preset was restored. The earlier strict no-head-motion check is superseded by the user's request for subtle attention: quiet head/neck movement is bounded and head roll remains prohibited.
- New clips join the currently rendered pose using bounded outgoing velocity and a quintic transition. Queued speech explicitly carries continuation intent while retaining fresh utterance identities. Waiting for the next inference retains the outgoing pose; an absent/failed successor eventually settles. Cancel still drops stale audio/motion and begins settling immediately. A stable stance origin prevents repeated idle/action cycles from accumulating pelvis sway as locomotion.
- The native 32-second idle capture ran at approximately 60 FPS, with 3.31 cm of pelvis travel, irregular blinks and multiple independent attention changes. Planted-foot drift was below 1 mm. A recorded-pose A → hold → pose B diagnostic saw zero intermediate idle frames; maximum per-frame joint/root steps were below 2 degrees / 4 mm. Three repeated held/clip/idle cycles retained the stance within 1 mm. These are measured continuity checks, not subjective human-like acceptance.
- Real Windows RTX 3070 StreamTalk inference → WKWebView audio → Unity passed for three speech jobs, including a queued transition. The continuation interval reported `joining` and zero idle frames; media-clock interpolation, pause, resume, completion and explicit interruption passed. Only TTS was replaced by an actual prerecorded voice WAV; no new live TTS/provider voice-quality claim is made.
- Seven performance-plan scenarios retained intended expressions and explicit cues; quiet speech produced no implicit semantic arm gestures or thinking tilt. Camera button/slider/lock/reset/zoom checks passed. Idle camera tracking now uses the planted stance instead of cancelling pelvis sway. Diagnostic profiles do not overwrite saved framing.
- The existing cloth topology, shoulder/sleeve attachment and solver settings remain unchanged. Preserved-hem regression covered standing, waving, return, turn, model motion, settling and disable/re-enable; all samples were finite, visible seam gap was zero, and maximum seam-skinning deviation was 7.01e-7 metres. Front/back/side captures were inspected. This does not claim every possible pose is free of garment intersections.
- Frontend and Unity release builds and installed deep/strict signature verification passed. Director tests: 51 passed, 1 external NSIS fixture skipped. Local evidence is under ignored `artifacts/natural-idle/`, `artifacts/streamtalk-native/` and the existing performance/conversation verification directories. No private model data or settings are published.

Reproduce the native idle, transition and stance-drift diagnostic (writes JSON/PNG, then exits):

```sh
"$HOME/Applications/Yachiyo Motion Studio.app/Contents/MacOS/Yachiyo Motion Studio" \
  --no-auto-connect --verify-natural-motion /absolute/output/prefix
```

The real-inference queued-speech regression uses `node director/tools/desktop/verify-streamtalk.mjs /absolute/path/spoken.wav`. It requires the running GPU service and the built private Mac application.


## 2026-11-13 — AMD desktop deployment and queued neural speech

- RX 9070 XT (about 15.92 GiB VRAM), Ryzen 7 9800X3D, Windows 11 Pro: deployed DiP + StreamTalk to `D:\YachiyoMotion`. The existing Windows ROCm Python was copied into a separate runtime. Original GPT-SoVITS PID 14080, port 9880, Torch and transformers versions were preserved through motion stop/start. This verifies lifecycle isolation, not sustained simultaneous inference throughput.
- All 106 model files (2,000,457,031 bytes) matched source hashes, including the separately licensed SMPL-X asset. Private weights, credentials, recordings, runtime and verification manifests are excluded from Git. No auto-start task/service was installed.
- Manual stop closed motion port 8787; restart produced a new motion PID; repeated start retained that PID. Three desktop shortcuts invoke the same start/stop/status controls.
- Real desktop DiP generated three consecutive 2-second clips (wave/walk/stand) in 0.1119 / 0.0879 / 0.0884 seconds. Finite poses, continuation, cancellation and old-stream rejection passed. Existing laptop Windows inference also passed.
- Native installed Mac backend verification passed: laptop generation → switch cancels old motion → three desktop chunks → laptop reconnect → desktop reconnect/save. Ubuntu is retained as an option but its new-version physical-machine acceptance remains deferred by the user.
- Speech now prefetches only the next queued neural job during current playback, reuses it at the handoff, and streams subsequent frames under the real media clock. No automatic `thinking` state or artificial inter-turn playback delay is sent for Unity. Cancellation, expiry, backend changes and late prefetch disposal are covered by focused tests.
- Real desktop GPU → WKWebView voice → Unity test passed for three jobs: first-window inference 1.2666 / 0.3121 / 0.3229 seconds in the latest run. The next job was requested 6.877 seconds before the preceding audio ended; next audio started 56 ms after that end. The handoff reported `model`, zero sampled underruns and zero intermediate idle frames. Pause/resume/completion/cancel passed. This replaces only TTS with a prerecorded six-second voice WAV, not live LLM/TTS. It does not guarantee no waiting when future text or audio has not arrived.
- Director checks: 60 passed, 1 external NSIS fixture skipped. Python service checks: 19 passed. Frontend/release builds and installed deep/strict signing passed. Updated frontend is packaged with the verified native build; no garment, shoulder/sleeve attachment or private scene rebuild occurred.
- Local evidence: ignored `artifacts/desktop/` (including native-switch, lifecycle, model integrity, inference and build logs) and `artifacts/streamtalk-native-desktop/result.json`. Reproduce using `YACHIYO_VERIFY_BACKEND=desktop node director/tools/desktop/verify-streamtalk.mjs /absolute/path/spoken.wav`; it asserts submission before prior audio end, handoff under 400 ms, streamed motion and cancellation.
