# Third-party assets

- StreamTalk: official `Xiangyue-Zhang/StreamTalk`, pinned source `550faa3d2fcf56d2055552fef6a19a107c998d44`, MIT; see `licenses/StreamTalk-MIT.txt`. The unmodified speaker-2 CFG-3 checkpoint is fetched from `X-Zhang/StreamTalk` revision `35a40bc4ddd968afefc79bec23aff2331ef539dc` with SHA256 verification. The bundled official face network is used internally by the motion sampler; FLAME expressions are not presented as native avatar facial blendshapes.
- StreamTalk external assets: WavLM `patrickvonplaten/wavlm-libri-clean-100h-large`, revision `e70e3a062ec399c46008ee55d1fb52c7ba338d5c`; BEAT2 English speaker-2 training retrieval motions, revision `8689ecb43513ba31964fd60e0ca69be02d3b0872`. They retain their upstream model/data terms and are not redistributed in Git or the Mac client.
- SMPL-X neutral with FLAME 2020: obtained separately from the official SMPL-X download page by the licensee. `SMPLX_NEUTRAL_2020.npz`, derived retrieval caches and model assets remain private. The source MIT license does not grant rights to redistribute these assets.

- Character: supplied by the user in `月见一万代/月见一万代_FT_10_12.unitypackage`. Original author names/paths are preserved. No redistribution license was supplied. Keep the original package, extracted character assets and built app private until the applicable model terms are confirmed.
- Kimodo source: NVIDIA, Apache-2.0, pinned commit `e1c219c99b4b94ba48be1fb6a1cbc64ecee4c954`; full license at `licenses/Kimodo-Apache-2.0.txt`. The embedded demonstration motion is converted from the official repository's `06_root_waypoints/motion.npz`, with its provenance shown in the application.
- Kimodo checkpoint and training data have separate licenses; see the linked official model card. No model weights are included in the Unity app.
- Noto Sans CJK SC: SIL Open Font License 1.1. Font and full license are bundled under `UnityProject/Assets/Resources`. Source: https://github.com/notofonts/noto-cjk.
- The optional text encoder depends on Meta Llama 3 and McGill-NLP LLM2Vec adapters. Model access and license acceptance are handled through the user's Hugging Face account. This optional encoder is not used by the default realtime service.

- DiP / Motion Diffusion Model: Guy Tevet and contributors, MIT source license, pinned commit `ef8edce6a53c6ab19e53b4d4dcf15bc0bc60a778`; see `licenses/DiP-MIT.txt`. Public checkpoint/statistics/initial context are downloaded from `guytevet/CLoSD` and retain their upstream model/data terms. The transformer and diffusion weights are unmodified. `tools/prepare_dip.py` only removes the unused SMPL rendering dependency from HumanML inference and pins the text encoder.
- DistilBERT: `distilbert/distilbert-base-uncased`, revision `12040accade4e8a0f71eabdb258fecc2e7e948be`, Apache-2.0; used as the model's trained text encoder.
- ARDY: NVIDIA source and Core skeleton skin asset, Apache-2.0, pinned commit `e706e27acc88070918308a10922b8f7174d0825e`; see `licenses/ARDY-Apache-2.0.txt`. Optional inference calls the public `hugging-apps/ardy` Space; that service can change independently of the pinned local adapter.
- Unity Cloth / PhysX is provided by the installed Unity editor/runtime. No Magica Cloth or Obi assets were purchased, copied or used.

- Director source snapshot: redchenk/yachiyo-live2d-studio v0.2.6, commit 7cfc08a328be332d3b876a4257e74f8411f08d5f, MIT; see director/LICENSE and director/UPSTREAM.md. Character model assets are excluded.
- Node runtime for the private macOS package: Node.js 24.22.2, official nodejs.org distribution, verified SHA256; its complete license is bundled as runtime/NODE-LICENSE. The runtime binary is downloaded during packaging and is not committed.
- The native host uses system AppKit, WebKit and QuartzCore frameworks. No third-party embedded browser SDK is redistributed.
- The director stage illustration is copied from the upstream director UI assets to align the Unity background with that UI; it contains no character mesh or texture data.
