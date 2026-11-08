#!/usr/bin/env bash
set -euo pipefail
motion_host="${YACHIYO_WINDOWS_HOST:-lenovo@192.168.5.9}"
motion_socket="/tmp/yachiyo-windows-${UID}.sock"
motion_port="${YACHIYO_WINDOWS_PORT:-18787}"
if ! ssh -S "$motion_socket" -O check "$motion_host" >/dev/null 2>&1; then
  echo '请输入笔记本 Windows 的登录密码（不会保存密码）。'
  ssh -o ControlMaster=auto -S "$motion_socket" -o ControlPersist=1800 \
    -o StrictHostKeyChecking=yes -o ConnectTimeout=8 -o ExitOnForwardFailure=yes \
    -fNT -L "$motion_port":127.0.0.1:8787 "$motion_host"
else
  # An installation session can exist before the client forward is added.
  if ! nc -z -w 1 127.0.0.1 "$motion_port"; then
    ssh -S "$motion_socket" -O forward -L "$motion_port":127.0.0.1:8787 "$motion_host"
  fi
fi
if ! curl --fail --silent --max-time 5 "http://127.0.0.1:$motion_port/health"; then
  echo '请先在 Windows 的 E:\YachiyoMotion 中双击「启动动作服务.cmd」。'
  exit 3
fi
echo
echo "Windows 动作地址：http://127.0.0.1:$motion_port"
