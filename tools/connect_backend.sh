#!/usr/bin/env bash
set -u
umask 077
motion_backend="${1:-ubuntu}"
case "$motion_backend" in windows|ubuntu|desktop) ;; *) exit 2 ;; esac
motion_tools="$(cd "$(dirname "$0")" && pwd)"
motion_state_dir="$HOME/Library/Application Support/Tsukuyomi/MotionConnections"
mkdir -p "$motion_state_dir"
motion_state="$motion_state_dir/$motion_backend.status"
write_result() { printf '%s\n' "$1" > "$motion_state.tmp"; mv "$motion_state.tmp" "$motion_state"; }
write_result connecting
if [[ "$motion_backend" == desktop ]]; then
  bash "$motion_tools/connect_desktop.sh"
elif [[ "$motion_backend" == windows ]]; then
  bash "$motion_tools/connect_windows.sh"
else
  bash "$motion_tools/connect_laptop.sh"
fi
motion_result=$?
if [[ "$motion_result" == 0 ]]; then
  write_result ready
  echo '连接完成，可以返回八千代。'
elif [[ "$motion_result" == 3 ]]; then
  write_result service_off
  echo '连接通道已准备好。请先在 Windows 启动动作服务，再回到八千代点击重连。'
else
  write_result failed
  echo '未能连接。请确认目标电脑和系统与所选服务一致，并检查登录是否完成。'
fi
exit "$motion_result"
