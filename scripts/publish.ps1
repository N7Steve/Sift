$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
& "$PSScriptRoot/build.ps1"
& "$PSScriptRoot/test.ps1"
$out = Join-Path $repo 'artifacts/Sift-win-x64'
dotnet publish src/Sift/Sift.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item README.md,LICENSE,THIRD-PARTY-NOTICES.md -Destination $out
Copy-Item docs -Destination $out -Recurse -Force
Compress-Archive -Path "$out/*" -DestinationPath (Join-Path $repo 'artifacts/Sift-win-x64.zip') -Force
Write-Host "Portable: $out/Sift.exe"
