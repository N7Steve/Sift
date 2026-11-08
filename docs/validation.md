# Validation record — 2026-11-05

Environment: Windows 11 x64 build 26200; .NET SDK 10.0.403, runtime 10.0.14; physical machine with working render audio. No administrator privileges or audio system changes used.

## Executed

- Release solution build: zero compiler warnings/errors.
- 23 xUnit cases passed: absolute silence, zero length, continuous/quiet audio, abrupt attack/end, separated/consecutive activity, threshold hysteresis, state transitions, exact pre/post-roll, arbitrary packet fragments including single-byte chunks, independent edge trim, processing disabled, brief-event options, segment minimum, overlapping margins, invalid settings/truncated frames.
- WAV reader validation: rate/channels/bit depth and PCM payload match the exact planned source frames.
- FLAC `--verify` encoding followed by raw decoding: bytes exactly equal expected PCM, including concatenated intervals and a Unicode path.
- Exclusive creation refuses an existing WAV; invalid destination reports a filesystem error.
- Real process loopback, repeated WAV/FLAC/WAV sessions: two separate helper processes play simultaneous 440 Hz and 997 Hz tones. Capturing the 440 Hz process gives amplitude 0.15000000 at 440 Hz and 0.00000000 at 997 Hz at displayed precision. Test requires at least 60 dB rejection. This is real Windows audio capture, not simulated packets.
- Real silent process: a helper with no render streams produced no retained file with trimming enabled.
- Real process exit: terminated the test source during capture; SourceExited detected it and Stop produced the captured partial recording.
- Native recorder disposal completes over repeated sessions; no teardown hangs. A formal native handle-leak soak test remains pending.
- Portable self-contained executable launched successfully. UI Automation refreshed/discovered the helper, selected its PID-backed item, set the output folder, invoked Grabar, observed Grabando/activity, invoked Detener and observed a finalized FLAC result. Window closed normally. Test restores previous settings. Real screenshot in screenshot.png.
- Visual inspection caught and corrected initial background, contrast, Unicode and primary-button visibility issues. Meter, timers and Stop are pinned outside the settings scroll region. Windows dark title bar and original application icon are configured.

Reproduce using `scripts/test.ps1`, `scripts/validate-audio.ps1` and (interactive desktop only, plays tones) `scripts/smoke-ui.ps1` after `scripts/publish.ps1`. Test outputs under artifacts/ are local and not committed. CI runs build/processing/codec tests and packages a preview; it cannot establish physical endpoint behavior.

## Pending manual / extended matrix

- [ ] Browser with several tabs/windows: verify and explain shared-process capture, repeat with Spotify/VLC/Discord and games.
- [ ] Background/minimized real applications, protected content, elevated applications and alternative audio drivers.
- [ ] Device hotplug, output switch and audio-service restart: confirm preservation and understandable error.
- [ ] Eight-hour recording, stable memory/CPU/handle count; RF64 >4 GiB readback and external-player compatibility.
- [ ] Disk full, destination disconnected/permission revoked during capture/export, interruption during FLAC; confirm all recoverable PCM is preserved.
- [ ] Hard termination/power-loss recovery using the PCM+JSON sidecar and recover.ps1. Buffered data cannot be guaranteed after power loss.
- [ ] 125/150/200% DPI, multiple displays, large system fonts, screen reader, high contrast and full keyboard-only workflow.
- [ ] Closing while recording/finalizing, rapid user start/stop, application root exit with surviving children.
- [ ] Listening tests with very quiet speech/music/effects to tune detection. Defaults intentionally preserve brief sounds; optional brief-edge filtering may remove legitimate effects.

No stable release tag is issued. Fixed PCM24 48k stereo is an explicit compatibility decision; automatic source-native capture is not implemented. No all-hardware or hours-long reliability claim.

## 0.2 format extension

- 46 automated cases pass, including all six bitrates (96/128/160/192/256/320 kbps) for both MP3 and OGG on this Windows machine.
- Every MP3 frame declares the selected CBR and 48 kHz. Windows decodes every output; expected codec padding is bounded by four 1152-sample stereo frames. Every OGG output decodes with independent NVorbis to exactly 48,000 stereo frames for one second of retained audio, including a join between two source intervals. Its nominal bitrate header matches the selected target.
- Decoded content is finite, has expected energy and the retained tone; deliberately omitted audio is excluded. Unicode paths, exclusive creation and invalid bitrates are tested. Existing WAV/FLAC byte-exact roundtrip tests still pass.
- Original hardware/DPI/soak test items above remain pending. Windows N without Media Foundation is not available here for testing; MP3 reports an error instead of substituting a different bitrate/format.
- Real process-loopback sessions finalized MP3 and OGG successfully. Portable UI Automation also exercised MP3 at 320 kbps and OGG at 128 kbps through Grabar/Detener, with finalized files and visible bitrate selection. Test scripts restore previous user settings.
- Very short OGG (10 ms) decodes to the exact frame count. MP3 shorter than 100 ms is rejected before output creation, preserving PCM; the 100 ms boundary decodes at all six bitrates. No silence is automatically appended to make short MP3s playable.
