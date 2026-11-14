# Windows portable client

The launcher opens one Unity window. Unity owns its Node director and a private
WebView2 child window. A per-launch named pipe transports viewport and verification
messages; the stage aperture sends mouse input directly to Unity. The web profile
and director data live under `%LOCALAPPDATA%/YachiyoUnityDirector`.

The launcher reuses a healthy local motion service. Otherwise it starts only the
Python executable in the chosen `YachiyoMotion` folder, remembers that process,
and stops it when Unity exits. It does not install a service or scheduled task,
modify GPT-SoVITS, or bundle account configuration. Test automation may use
temporary interactive scheduled tasks, which are removed after verification.

Build with the saved private `UnityProject/Assets/Yachiyo/Studio.unity` and its
licensed character assets. Do not regenerate the scene to package a release.

1. Build `director` with its existing Vite build command.
2. Run `python tools/fetch_windows_runtime.py`.
3. On Windows, run:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File native/windows/build.ps1 -Sdk artifacts/windows-client/webview2-sdk -Output artifacts/windows-client/native
   ```

   If Unity is built on a Mac, copy only the resulting `native` output directory
   back to `artifacts/windows-client/native` on that Mac.
4. Install Windows Mono support matching Unity 6000.6.4f1, then run the editor:

   ```text
   Unity -batchmode -projectPath <absolute UnityProject path> -buildTarget Win64 -executeMethod Yachiyo.Editor.BuildStudio.BuildWindows -quit -logFile <absolute build log>
   ```

5. Run `python tools/package_windows.py`. It validates scene files, includes
   pinned Node and Microsoft runtime notices, writes a SHA256 inventory, and
   produces `artifacts/windows-client/Yachiyo-Windows-x64.zip`.

The ZIP contains private character assets and must not be attached to a public
GitHub release. The normal launcher is `启动八千代.exe`; `Yachiyo.exe` is the
underlying player and bypasses motion lifecycle management if launched directly.

Validation: run director tests (including native localhost routing), check the
real Windows build in an interactive session, play real StreamTalk-generated
frames, drag/rotate/zoom the stage, and check both reuse and owned-service exit.
Verification flags use a separate WebView profile and do not save camera changes.

Microsoft API reference: https://learn.microsoft.com/microsoft-edge/webview2/get-started/winforms
SDK package: https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35
