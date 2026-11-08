#!/usr/bin/env bash
set -u
cd "$(dirname "$0")"
bash tools/connect_windows.sh
echo '在八千代的「场景设置 F6」中选择 Windows 即可，无需填写地址。'
echo '此入口只连接手动启动的 Windows 服务，不会自动启动模型。'
read -r -p '按回车关闭窗口…'
