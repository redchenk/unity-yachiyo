@echo off
chcp 65001 >nul
title 八千代动作服务
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows\control.ps1" -Action start
pause
