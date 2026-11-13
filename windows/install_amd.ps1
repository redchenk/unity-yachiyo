param([Parameter(Mandatory=$true)][string]$BasePython)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=Split-Path $PSScriptRoot -Parent
$source=Split-Path (Resolve-Path $BasePython).Path -Parent
$runtime=Join-Path $root 'runtime'
$marker=Join-Path $root 'state\amd-runtime.json'
New-Item (Join-Path $root 'state'),(Join-Path $root 'logs'),(Join-Path $root 'cache\tmp') -ItemType Directory -Force | Out-Null
if (!(Test-Path $marker)) {
    if (Test-Path $runtime) { throw 'Existing runtime has no AMD ownership marker; not overwritten.' }
    & $BasePython -c "import torch; assert torch.version.hip and torch.cuda.is_available(); print(torch.__version__,torch.cuda.get_device_name())"
    if ($LASTEXITCODE) { throw 'The source must be a working Windows ROCm Python environment.' }
    # Copy the environment: later motion-package installs cannot alter the TTS
    # environment, and stopping this service never stops the source service.
    & robocopy $source $runtime /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XJ
    if ($LASTEXITCODE -ge 8) { throw 'Runtime copy failed; inspect the partial destination before retrying.' }
    @{source=$source;root=$runtime;created=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content $marker -Encoding UTF8
}
$python=Join-Path $runtime 'python.exe'
$env:PYTHONUTF8='1';$env:PYTHONNOUSERSITE='1'
$env:TEMP=Join-Path $root 'cache\tmp';$env:TMP=$env:TEMP
$env:PIP_CONFIG_FILE='NUL';$env:PIP_INDEX_URL='https://pypi.org/simple';$env:PIP_EXTRA_INDEX_URL=''
$env:PIP_CACHE_DIR=Join-Path $root 'cache\pip'
$wheelhouse=Join-Path $root 'windows\amd-wheels'
$pipSource=if(Test-Path $wheelhouse){@('--no-index','--find-links',$wheelhouse)}else{@('--timeout','60')}
# PowerShell 5 treats native stderr warnings as errors when output is redirected.
# Check pip's exit code rather than aborting on its progress/warning messages.
$ErrorActionPreference='Continue'
& $python -m pip uninstall -y gradio
& $python -m pip install --disable-pip-version-check --progress-bar off @pipSource transformers==5.8.1 smplx==0.1.28 positional-encodings==6.0.3 joblib==1.6.0 gradio-client==2.7.2
$pipExit=$LASTEXITCODE
$ErrorActionPreference='Stop'
if ($pipExit) { throw 'Motion dependencies could not be installed.' }
& $python -c "import torch,smplx; assert torch.version.hip and torch.cuda.is_available(); print(torch.__version__,torch.cuda.get_device_name(),torch.__file__)"
if ($LASTEXITCODE) { throw 'Copied ROCm runtime verification failed.' }
@{date=(Get-Date).ToString('o');root=$root;provider='dip';accelerator='rocm';manualOnly=$true} | ConvertTo-Json | Set-Content (Join-Path $root 'state\installed.json') -Encoding UTF8
Write-Host 'AMD runtime ready. Model files must also be copied and verified before starting.'
