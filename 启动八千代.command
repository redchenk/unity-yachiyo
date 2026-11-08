#!/bin/bash
set -eu
cd "$(dirname "$0")"
yachiyo_app="$HOME/Applications/Yachiyo Motion Studio.app"
if [[ ! -d "$yachiyo_app" ]]; then yachiyo_app="$PWD/build/Yachiyo Motion Studio.app"; fi
open "$yachiyo_app"
