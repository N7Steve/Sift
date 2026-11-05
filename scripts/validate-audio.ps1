$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
dotnet run --project tests/Sift.Probe -c Release -- validate artifacts/validation
if ($LASTEXITCODE -ne 0) { throw 'Real process-loopback validation failed.' }
