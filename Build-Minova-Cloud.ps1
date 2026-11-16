param([string]$DotNet)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
if(-not $DotNet){$localSdk=Join-Path $env:LOCALAPPDATA 'Minova Cloud\BuildTools\dotnet\dotnet.exe';if(Test-Path -LiteralPath $localSdk){$DotNet=$localSdk}else{$DotNet=(Get-Command dotnet).Source}}
$project=Join-Path $PSScriptRoot 'src\Minova.Cloud.Windows\Minova.Cloud.Windows.csproj'
[xml]$projectXml=Get-Content -LiteralPath $project -Raw;$version=[string]$projectXml.Project.PropertyGroup.Version
$work=Join-Path $PSScriptRoot '.work';$publish=Join-Path $work 'publish';$release=Join-Path $PSScriptRoot 'releases'
New-Item -ItemType Directory -Force -Path $work,$publish,$release | Out-Null
& $DotNet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $publish --nologo
if($LASTEXITCODE -ne 0){throw 'Windows app compilation failed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'installer\Uninstall.ps1') -Destination $publish -Force
$uninstallerHash=(Get-FileHash -LiteralPath (Join-Path $publish 'Uninstall.ps1') -Algorithm SHA256).Hash
$payload=Join-Path $work 'Payload.zip';if(Test-Path -LiteralPath $payload){Remove-Item -LiteralPath $payload}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish,$payload,[IO.Compression.CompressionLevel]::Optimal,$false)
(Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath (Join-Path $work 'Payload.sha256') -Encoding ASCII
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$setup=Join-Path $release 'Minova-Cloud-Setup.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll "/win32icon:$PSScriptRoot\src\Minova.Cloud.Windows\Assets\minova.ico" "/win32manifest:$PSScriptRoot\src\Minova.Cloud.Windows\app.manifest" "/resource:$payload,Payload.zip" "/resource:$work\Payload.sha256,Payload.sha256" "/out:$setup" (Join-Path $PSScriptRoot 'installer\Setup.cs')
if($LASTEXITCODE -ne 0){throw 'Windows installer compilation failed.'}
Copy-Item -LiteralPath $setup -Destination (Join-Path $release "Minova-Cloud-Desktop-$version.exe") -Force
$checksum=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
@{version=$version;platform='windows-x64';file='Minova-Cloud-Setup.exe';sha256=$checksum;sourceCommit=(& git rev-parse HEAD 2>$null)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $release 'release.json') -Encoding utf8
"$checksum  Minova-Cloud-Setup.exe`n$checksum  Minova-Cloud-Desktop-$version.exe" | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Installer ready: $setup"
exit 0
