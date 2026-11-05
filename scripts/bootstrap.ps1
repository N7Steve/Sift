$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0' }
if (-not (Test-Path tools/codec/flac.exe)) { throw 'Missing bundled FLAC. Restore tools/codec from Git.' }
dotnet restore Sift.slnx
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
