# Architecture decisions

- `Sift.Core/Capture`: NAudio WasapiRecorderBuilder.WithProcessLoopback(pid), IncludeTargetProcessTree only, BuildAsync. Windows 11 minimum. Never calls endpoint or microphone fallback APIs. Native callback copies its transient span and tries a bounded channel (256 packets). It never waits for file I/O, analysis or UI. Queue overflow/discontinuity makes the session explicitly fail, preserving raw data.
- `Detector`: deterministic PCM24 RMS windows, fixed 480-frame stereo windows, entrance threshold -45 dBFS and exit -51 dBFS, 1.5s grouping timeout. Records sample-frame intervals. Packet fragments are accumulated; margins are read back from disk, equivalent to a pre-roll buffer without retaining the entire session in memory. Each activity event is held until the timeout; recent sound reopens the same sequence. Post-roll defaults to 200 ms. Events/margins are merged to prevent duplicate frames.
- `Session`: a worker drains the bounded queue to a sequential spool, analyzes and publishes estimates. The UI polls at 10 Hz. Source identity includes process start time to avoid PID-reuse confusion. Source exit finalizes; discontinuities and disk failures retain recovery data. No unbounded sample buffer; event metadata grows with the number of independent sequences.
- `Export`: after capture stops, seeks retained intervals and copies their bytes into WAV. RIFF reserves ds64 space and switches to RF64 for large sizes. FLAC runs as an independent official executable with `--verify`; temporary outputs are promoted only after completion. Buffers are 1 MiB independent of session size. Files are created with exclusive new names and never overwrite.
- WPF presentation owns discovery, preferences and session orchestration. No DI framework, database, event bus or web layer. Icons use shell extraction, with native handles destroyed after creating frozen WPF images. Audio-session meters are only discovery hints.
- Preferences use atomic same-directory temporary-file replacement. Audio is never stored in the settings folder. Invalid JSON/detection settings revert to defaults.

## API verification

NAudio 3.3.0 was restored from NuGet and its actual public API compiled/executed. `WithProcessLoopback(...).WithFormat(new WaveFormat(48000,24,2)).BuildAsync()` worked on Windows build 26200. The callback is `(ReadOnlySpan<byte>, AudioClientBufferFlags, ulong devicePosition, ulong qpcPosition)`; spans are copied during the callback. Disposal is asynchronous.

Microsoft documents process-loopback starting at build **20348**: https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/ . NAudio's builder annotation/documentation says **19041**. Sift adopts Windows 11 **22000** as the supported minimum, rather than trusting the lower library annotation. No Windows 10 support claim.

NAudio docs: https://naudio.github.io/NAudio/docs/WasapiRecorder.html
FLAC: https://github.com/xiph/flac/releases/tag/1.5.0

## Tradeoffs

A disk spool makes independent edge operations, recovery and exact concatenation simple and testable, at the cost of disk usage and deferred finalization. Source streams can differ in rate/channel count; a declared fixed Windows capture format avoids pretending to infer a single source-native format for a process tree. FLAC retains every captured PCM bit. A future float/native capture mode must define conversion semantics separately, not relabel float as lossless integer PCM.

## MP3 and OGG export (0.2)

`AudioOutput` validates format/bitrate and reads retained PCM intervals with 64-bit offsets and bounded buffers. MP3 uses NAudio's MediaFoundationEncoder stream overload, with CreateNew for output protection. Supported Windows bitrates are checked before capture so the selected CBR is never silently substituted. PCM24 -> normalized float -> PCM16 quantization applies exclusively to the lossy MP3 encoder; there is no resampling, gain adjustment or change to WAV/FLAC capture fidelity.

OGG uses native Xiph libogg 1.3.5/libvorbis 1.3.7, OggVorbisSharp 1.0.1 (MIT ABI bindings), and vorbis_encode_init with nominal bitrate and unconstrained min/max. This is an average target, not strict CBR. Analysis/bitrate blocks are completely drained at EOS; granule positions carry the exact duration. Native states are cleared in finally blocks even if writing fails. PCM24 values are exactly representable as normalized float32 for Vorbis input. No managed candidate with failing bitrate/EOS tests is included.

The native DLLs in tools/vorbis are built from the included unmodified official Xiph source archives using MSVC 19.44, Release x64, static CRT (/MT). PE imports were checked: only Kernel32 and the other bundled Xiph DLLs are required; users do not need a Visual C++ runtime install. Optional scripts/build-vorbis.ps1 reproduces the builds using VS 2022 C++ Build Tools/CMake. Normal .NET builds and CI use the bundled DLLs and do not require C++ tools. Native upstream sources emit existing numeric-conversion compiler warnings; Sift's managed Release build remains warning-free. Xiph BSD notices accompany the package. The libvorbis API's C long parameters are 32-bit on the supported Windows x64 ABI.

References: https://xiph.org/vorbis/doc/vorbisenc/vorbis_encode_init.html , https://xiph.org/vorbis/doc/libvorbis/overview.html , https://naudio.github.io/NAudio/api/NAudio.Wave.MediaFoundationEncoder.html .
