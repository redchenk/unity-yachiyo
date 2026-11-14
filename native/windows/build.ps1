param([string]$Output,[string]$Sdk)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item $Output -ItemType Directory -Force | Out-Null
$managed=Join-Path $Sdk 'lib\net462'
Copy-Item (Join-Path $managed 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $managed 'Microsoft.Web.WebView2.WinForms.dll') $Output -Force
Copy-Item (Join-Path $Sdk 'runtimes\win-x64\native\WebView2Loader.dll') $Output -Force
& $compiler /nologo /target:winexe /platform:x64 /optimize+ ("/out:"+(Join-Path $Output 'YachiyoWebHost.exe')) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ("/r:"+(Join-Path $Output 'Microsoft.Web.WebView2.Core.dll')) ("/r:"+(Join-Path $Output 'Microsoft.Web.WebView2.WinForms.dll')) (Join-Path $PSScriptRoot 'WebHost.cs')
if($LASTEXITCODE){throw 'WebView host build failed'}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ ("/out:"+(Join-Path $Output 'YachiyoLauncher.exe')) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'Launcher.cs')
if($LASTEXITCODE){throw 'Launcher build failed'}
