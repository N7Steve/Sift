$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
dotnet build Sift.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
