param([Parameter(Mandatory=$true)][string]$Pcm,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
$Pcm = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Pcm)
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
if (-not (Test-Path -LiteralPath $Pcm)) { throw 'PCM recovery file does not exist.' }
$fullPcm = (Resolve-Path -LiteralPath $Pcm).Path
dotnet run --project tests/Sift.Probe -c Release -- recover $fullPcm $Output
if ($LASTEXITCODE -ne 0) { throw 'Recovery failed. The source PCM was preserved.' }
