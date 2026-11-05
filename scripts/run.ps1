$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
dotnet run --project src/Sift/Sift.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Sift could not run.' }
