Sift source is MIT licensed. Bundled components retain their own licenses.

- NAudio 3.3.0: MIT, Copyright Mark Heath and contributors, https://github.com/naudio/NAudio . The MIT permission and warranty terms in LICENSE apply with this copyright notice.
- .NET / WPF runtime: MIT and third-party notices included in the self-contained runtime distribution. https://github.com/dotnet/runtime and https://github.com/dotnet/wpf
- FLAC 1.5.0 official unmodified Windows x64 binaries, https://github.com/xiph/flac/releases/tag/1.5.0 . flac.exe is GPL v2 or later; libFLAC.dll is BSD licensed. These are independent external programs, invoked over files, not linked into Sift. License texts, upstream build notes and the complete corresponding FLAC 1.5.0 source archive are included in codec/. See COPYING.GPL and COPYING.Xiph. No FLAC source modifications were made.

Upstream binaries: https://github.com/xiph/flac/releases/download/1.5.0/flac-1.5.0-win.zip
Corresponding source: https://github.com/xiph/flac/releases/download/1.5.0/flac-1.5.0.tar.xz

- OggVorbisSharp 1.0.1: MIT (as declared by its NuGet package), https://github.com/SamboyCoding/OggVorbisSharp . Permission/warranty terms in codec/licenses/OggVorbisSharp-MIT.txt.
- Xiph libogg 1.3.5 and libvorbis 1.3.7: BSD-style licenses, full notices in codec/licenses/Ogg-BSD.txt and Vorbis-BSD.txt. Built without source modifications from https://downloads.xiph.org/releases/ogg/libogg-1.3.5.tar.xz and https://downloads.xiph.org/releases/vorbis/libvorbis-1.3.7.tar.xz. Source archives are included in codec/sources. Rebuild instructions are in docs/architecture.md and scripts/build-vorbis.ps1. The x64 DLLs use the statically linked Microsoft C runtime supplied with Visual Studio Build Tools.
- MP3 encoding uses Windows Media Foundation; no external MP3 binary is bundled.
- NVorbis 0.10.5 is an MIT-licensed test-only independent decoder, not included in the portable app.
