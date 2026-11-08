#!/usr/bin/env bash
set -euo pipefail
motion_host="${YACHIYO_HOST:-lenovo@192.168.5.9}"
motion_socket="/tmp/yachiyo-motion-${UID}.sock"
motion_key="$HOME/.ssh/codex-lenovo-ubuntu2404"
motion_known_hosts="$HOME/.ssh/known_hosts_codex_lenovo_ubuntu"
if ! ssh -S "$motion_socket" -O check "$motion_host" >/dev/null 2>&1; then
  ssh -o ControlMaster=auto -S "$motion_socket" -o ControlPersist=1800 \
    -o HostKeyAlias=lenovo-ubuntu2404-install -o UserKnownHostsFile="$motion_known_hosts" \
    -o StrictHostKeyChecking=yes -o BatchMode=yes -o ConnectTimeout=7 \
    -o ExitOnForwardFailure=yes -i "$motion_key" \
    -fNT -L 8787:127.0.0.1:8787 "$motion_host"
fi
if ! nc -z -w 1 127.0.0.1 8787; then
  ssh -S "$motion_socket" -O forward -L 8787:127.0.0.1:8787 "$motion_host"
fi
ssh -S "$motion_socket" "$motion_host" systemctl --user start yachiyo-motion.service
curl --fail --silent --max-time 8 http://127.0.0.1:8787/health
echo
