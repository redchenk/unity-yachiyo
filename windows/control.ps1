param([ValidateSet('start','stop','status')][string]$Action='status')
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$root=Split-Path $PSScriptRoot -Parent
$python=Join-Path $root 'runtime\python.exe'
$entry=Join-Path $PSScriptRoot 'run_service.py'
$statePath=Join-Path $root 'state\service.json'
function Get-OwnedProcess {
    if (!(Test-Path $statePath)) { return $null }
    $state=Get-Content $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $process=Get-CimInstance Win32_Process -Filter "ProcessId = $($state.pid)"
    if ($process -and $process.ExecutablePath -eq $python -and
        $process.CommandLine.IndexOf($entry,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return $process
    }
    return $null
}
$owned=Get-OwnedProcess
if ($Action -eq 'stop') {
    if ($owned) {
        Stop-Process -Id $owned.ProcessId -ErrorAction Stop
        Wait-Process -Id $owned.ProcessId -Timeout 20 -ErrorAction SilentlyContinue
        Remove-Item $statePath -Force -ErrorAction SilentlyContinue
        Write-Host '动作服务已关闭，模型显存已释放。'
    } else { Write-Host '动作服务当前未运行。' }
    exit 0
}
if ($Action -eq 'start') {
    if (!(Test-Path (Join-Path $root 'state\installed.json'))) {
        throw '请先双击「首次安装.cmd」完成安装。'
    }
    if (!$owned) {
        $listener=Get-NetTCPConnection -LocalPort 8787 -State Listen -ErrorAction SilentlyContinue
        if ($listener) { throw '8787 端口正由其他程序使用，未启动第二份服务。' }
        New-Item (Join-Path $root 'logs') -ItemType Directory -Force | Out-Null
        $env:PYTHONUTF8='1';$env:PYTHONNOUSERSITE='1'
        # WMI creates an independent process outside an SSH session's job object.
        # This needs no scheduled task, service registration or automatic trigger.
        $created=Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=('"'+$python+'" -u "'+$entry+'"');CurrentDirectory=$root}
        if ($created.ReturnValue -ne 0) { throw ('启动进程失败：'+$created.ReturnValue) }
        for ($i=0;$i -lt 120;$i++) {
            if (!(Get-Process -Id $created.ProcessId -ErrorAction SilentlyContinue)) { throw '模型启动失败，请查看 logs\service-error.log。' }
            try {
                $health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 1
                if ($health.provider -eq 'dip' -and $health.state -eq 'ready') {
                    Write-Host '八千代动作服务已启动。需要停止时双击「关闭动作服务.cmd」。'
                    exit 0
                }
            } catch {}
            Start-Sleep -Seconds 1
        }
        throw '加载超过两分钟，请查看 logs 文件夹中的日志。'
    }
    Write-Host '动作服务已经启动，无需重复打开。'
    exit 0
}
if (!$owned) { Write-Host '动作服务：已关闭。'; exit 0 }
$health=Invoke-RestMethod 'http://127.0.0.1:8787/health' -TimeoutSec 3
Write-Host ('动作服务：'+$health.state+'；模型：'+$health.model)
Write-Host ('进程：'+$owned.ProcessId+'；目录：'+$root)
