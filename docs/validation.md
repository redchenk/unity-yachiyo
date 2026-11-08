# Validation — 2026-11-08

- Unity 6000.6.4f1 macOS build completed. The unified Apple Silicon application was locally signed and passed deep/strict signature verification.
- The original director UI was observed in the same native window, with the Unity avatar in its central stage and the original navigation, controls and captions.
- The actual WKWebView → local gateway → Unity test passed: parsed semantic LLM fixture, live audio start, all five real mesh visemes, no added JawOpen on vowels, singing pause/resume/backward seek/silence, explicit cancel and automatic owned-service shutdown.
- Peak A/I/U/E/O shape weights in the final native fixture run: 39.51, 42.75, 40.18, 41.80, 40.00. Closed phases returned to zero; the test uses synthetic calibrated audio, not a live voice provider.
- Windows laptop service reported DiP 10-step / DistilBERT / RTX 3070. A real generated wave reached the native avatar and returned accepted → started → completed. Ubuntu remains deferred at the user's request.
- Director unit/integration tests: 32 passed, 1 skipped (external NSIS compiler fixture). Motion service logic tests: 17 passed.
- In the final signed application, native camera dragging visibly rotated the central 3D avatar; reset restored the front view. Hide/show removed and restored the character while preserving the original garment renderer states.
- No real audience messages or paid LLM/TTS requests were sent. Human listening, microphone permission and live provider acceptance are separate from the synthetic audio checks.

Detailed runtime reports and screenshots stay in local artifacts/ and are not published with character assets or private settings.
