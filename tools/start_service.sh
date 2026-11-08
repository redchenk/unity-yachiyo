#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export HTTPS_PROXY="${HTTPS_PROXY:-http://127.0.0.1:7890}"
export HTTP_PROXY="${HTTP_PROXY:-http://127.0.0.1:7890}"
export NO_PROXY=localhost,127.0.0.1
export YACHIYO_ENCODER="${YACHIYO_ENCODER:-nf4}"
export HF_HUB_DISABLE_XET=1
export OMP_NUM_THREADS=4
motion_provider="${YACHIYO_PROVIDER:-dip}"
motion_python=.venv-ardy/bin/python
if [[ "$motion_provider" == "kimodo" ]]; then motion_python=.venv/bin/python; fi
exec "$motion_python" -m service.server --provider "$motion_provider" --host 127.0.0.1 --port 8787 "$@"
