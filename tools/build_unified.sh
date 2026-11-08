#!/usr/bin/env bash
set -euo pipefail
unified_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$unified_root"
unified_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$unified_editor" ]]; then echo '请安装 Unity 6000.6.4f1，或设置 UNITY_EDITOR。'; exit 1; fi
if [[ ! -f UnityProject/Assets/Yachiyo/Studio.unity ]]; then echo '请先导入角色，在 Unity 执行 Yachiyo/Create Studio。'; exit 1; fi
(cd director && npm run build)
python3 tools/prepare_unified.py
"$unified_editor" -batchmode -projectPath "$unified_root/UnityProject" -executeMethod Yachiyo.Editor.BuildStudio.BuildMac -quit -logFile "$unified_root/artifacts/unified-build.log"
python3 tools/package_unified.py
