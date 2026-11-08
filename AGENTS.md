# Working on Sift

- Work directly on main. Inspect status/remotes and fetch first; fast-forward only. Preserve existing work. Never force push or delete branches. Commit/push only when the user explicitly authorizes the concrete task scope.
- Windows 11 x64; .NET 10 SDK. `./scripts/build.ps1`, `./scripts/test.ps1`, `./scripts/run.ps1`, `./scripts/publish.ps1`. Scripts must locate the repository from PSScriptRoot.
- Core capture/detection/export in src/Sift.Core; WPF UI/discovery/preferences in src/Sift. Deterministic xUnit tests in tests/Sift.Tests. tests/Sift.Probe and scripts/validate-audio.ps1 perform real Windows audio validation, which audibly plays test tones.
- Never use global loopback or microphone as a fallback. Capture callbacks copy spans and enqueue without blocking. Report discontinuities and overflow. Keep sample buffers bounded and preserve recoverable audio on errors.
- No normalization, fades or silent format conversion. Fixed capture is PCM24 48k stereo; disclose Windows conversion. Interval operations must preserve bytes and frame counts.
- Native API changes require primary-source verification and real Windows validation. Compilation and synthetic tests alone do not establish WASAPI isolation.
- UI text is Spanish, documentation English. Files UTF-8; when piping Python through Windows PowerShell set OutputEncoding to UTF8 (its default ASCII corrupts accented text).
- No external services, telemetry, system changes or elevation. Do not alter other repositories. Do not overwrite completed recordings. Third-party binary changes require license, corresponding-source and provenance updates.
- Before delivery build Release, run tests and real capture when an endpoint is available. Record pending hardware/DPI/soak checks in docs/validation.md. Do not label an unvalidated build stable.

- MP3/OGG are lossy exports only: AudioOutput streams retained PCM intervals; do not change lossless capture bytes. MP3 uses Windows Media Foundation CBR with PCM16 conversion and a validated 100 ms minimum; OGG uses native Xiph Vorbis average bitrate with exact granules. Bitrates are 96/128/160/192/256/320 kbps. Rebuild native DLLs only via scripts/build-vorbis.ps1 and update provenance/licenses/checksums.
