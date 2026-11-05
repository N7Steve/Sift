$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
dotnet test tests/Sift.Tests/Sift.Tests.csproj -c Release --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/test-results
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
