"""Fetch pinned official runtimes used by the portable Windows client."""
from pathlib import Path
import hashlib,urllib.request,zipfile
ROOT=Path(__file__).resolve().parents[1]
out=ROOT/'artifacts/windows-client';out.mkdir(parents=True,exist_ok=True)
files=[
 ('node-win.zip','https://nodejs.org/dist/v24.22.2/node-v24.22.2-win-x64.zip','ca1bff5d8ead13c9f2ea404a176db46bd430704d5b5113734049fa365e7799e2'),
 ('webview2.nupkg','https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4314.35/microsoft.web.webview2.1.0.4314.35.nupkg','90778deb1b77e36292828b4b5833c9255c27533f40e0ddd55891ae718944edc4'),
]
for name,url,digest in files:
    file=out/name
    if not file.exists():
        part=file.with_suffix(file.suffix+'.partial');urllib.request.urlretrieve(url,part)
        if hashlib.sha256(part.read_bytes()).hexdigest()!=digest:raise SystemExit('Checksum mismatch: '+name)
        part.replace(file)
    if hashlib.sha256(file.read_bytes()).hexdigest()!=digest:raise SystemExit('Checksum mismatch: '+name)
with zipfile.ZipFile(out/'webview2.nupkg') as archive:archive.extractall(out/'webview2-sdk')
print('Windows Node and WebView2 SDK verified:',out)
