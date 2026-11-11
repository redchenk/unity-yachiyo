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
