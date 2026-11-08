# Optional: regenerate the bundled x64 codecs from the included unmodified Xiph sources.
# Requires CMake, tar and Visual Studio 2022 C++ Build Tools. Normal builds use bundled DLLs.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
$work = Join-Path $repo 'artifacts/native'
$prefix = Join-Path $work 'prefix'
New-Item -ItemType Directory -Force $work | Out-Null
foreach ($archive in @('libogg-1.3.5','libvorbis-1.3.7')) {
    tar -xf "tools/codec/sources/$archive.tar.xz" -C $work
    if ($LASTEXITCODE -ne 0) { throw 'Source extraction failed.' }
}
$common = @('-G','Visual Studio 17 2022','-A','x64','-DBUILD_SHARED_LIBS=ON','-DCMAKE_POLICY_VERSION_MINIMUM=3.5','-DCMAKE_POLICY_DEFAULT_CMP0091=NEW','-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded',"-DCMAKE_INSTALL_PREFIX=$prefix")
cmake -S "$work/libogg-1.3.5" -B "$work/ogg-build" @common '-DINSTALL_DOCS=OFF'
if ($LASTEXITCODE -ne 0) { throw 'Ogg configure failed.' }
cmake --build "$work/ogg-build" --config Release --target install
if ($LASTEXITCODE -ne 0) { throw 'Ogg build failed.' }
cmake -S "$work/libvorbis-1.3.7" -B "$work/vorbis-build" @common "-DCMAKE_PREFIX_PATH=$prefix"
if ($LASTEXITCODE -ne 0) { throw 'Vorbis configure failed.' }
cmake --build "$work/vorbis-build" --config Release --target install
if ($LASTEXITCODE -ne 0) { throw 'Vorbis build failed.' }
foreach ($dll in @('ogg.dll','vorbis.dll','vorbisenc.dll')) { Copy-Item "$prefix/bin/$dll" tools/vorbis -Force }
Get-ChildItem tools/vorbis -Filter '*.dll' | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName).Hash.ToLowerInvariant(),$_.Name } | Set-Content tools/vorbis/SHA256SUMS
