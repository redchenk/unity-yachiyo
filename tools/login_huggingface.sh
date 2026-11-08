#!/usr/bin/env bash
set -euo pipefail
cd "$HOME/YachiyoMotion"
export HTTPS_PROXY=http://127.0.0.1:7890
export HTTP_PROXY=http://127.0.0.1:7890
export NO_PROXY=localhost,127.0.0.1
echo '请使用已获 Meta-Llama-3-8B-Instruct 权限的 Hugging Face 账号。'
echo '访问 https://huggingface.co/settings/tokens 创建只读令牌，在下方本地输入。'
echo '令牌不会写入本项目；由 Hugging Face 的登录工具保存到本机账号缓存。'
.venv/bin/hf auth login
read -r -p '完成后按回车关闭。'
