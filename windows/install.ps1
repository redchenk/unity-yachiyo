$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$root=Split-Path $PSScriptRoot -Parent
if ((Split-Path $root -Qualifier) -ne 'E:') { throw '请将整个 YachiyoMotion 文件夹放到 E 盘后安装。' }
New-Item (Join-Path $root 'state'),(Join-Path $root 'logs') -ItemType Directory -Force | Out-Null
$python=Join-Path $root 'runtime\python.exe'
$env:PIP_CONFIG_FILE='NUL';$env:PIP_INDEX_URL='https://pypi.org/simple';$env:PIP_EXTRA_INDEX_URL='';$env:HF_HUB_DISABLE_IMPLICIT_TOKEN='1'
New-Item (Join-Path $root 'cache\tmp') -ItemType Directory -Force | Out-Null
$env:TEMP=Join-Path $root 'cache\tmp';$env:TMP=$env:TEMP
$env:PYTHONUTF8='1';$env:PYTHONNOUSERSITE='1';$env:PIP_CACHE_DIR=Join-Path $root 'cache\pip'
$env:HF_HOME=Join-Path $root 'models\huggingface';$env:HF_HUB_DISABLE_XET='1'
if (!(Test-Path $python)) {
    Expand-Archive (Join-Path $PSScriptRoot 'python-3.12.10-embed-amd64.zip') (Join-Path $root 'runtime') -Force
    @('python312.zip','.','Lib\site-packages','..','import site') | Set-Content (Join-Path $root 'runtime\python312._pth') -Encoding ASCII
}
if (!(Test-Path (Join-Path $root 'runtime\Lib\site-packages\pip'))) {
    if (!(Test-Path (Join-Path $PSScriptRoot 'get-pip.py'))) { Invoke-WebRequest 'https://bootstrap.pypa.io/get-pip.py' -OutFile (Join-Path $PSScriptRoot 'get-pip.py') -UseBasicParsing -TimeoutSec 60 }
    & $python (Join-Path $PSScriptRoot 'get-pip.py') --no-warn-script-location
    if ($LASTEXITCODE) { throw 'Python 依赖管理器安装失败。' }
}
Write-Host '正在准备独立的 NVIDIA 动作运行环境…'
& $python -m pip install --progress-bar off --timeout 30 --disable-pip-version-check --no-warn-script-location torch==2.6.0 --index-url https://download.pytorch.org/whl/cu124
if ($LASTEXITCODE) { throw 'PyTorch 安装失败。' }
& $python -m pip install --progress-bar off --timeout 30 --disable-pip-version-check --no-warn-script-location -r (Join-Path $root 'service\requirements-dip.txt')
if ($LASTEXITCODE) { throw '动作服务依赖安装失败。' }
& $python (Join-Path $PSScriptRoot 'prepare_models.py')
if ($LASTEXITCODE) { throw '模型下载或校验失败。' }
& $python -c "import torch; assert torch.cuda.is_available(), 'CUDA unavailable'; print(torch.__version__, torch.cuda.get_device_name(0))"
if ($LASTEXITCODE) { throw '未检测到可用的 NVIDIA 显卡，请检查驱动。' }
@{date=(Get-Date).ToString('o');root=$root;provider='dip';manualOnly=$true} | ConvertTo-Json | Set-Content (Join-Path $root 'state\installed.json') -Encoding UTF8
Write-Host '安装完成。双击「启动动作服务.cmd」即可使用；不会开机自启。'
