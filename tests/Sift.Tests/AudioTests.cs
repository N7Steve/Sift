using Sift.Core;
using NAudio.Wave;
namespace Sift.Tests;

public class AudioTests
{
    static byte[] Pcm(params (int ms, double level)[] parts)
    {
        using var s = new MemoryStream();
        foreach (var (ms, level) in parts)
            for (int i = 0; i < ms * 48; i++)
            {
                int v = (int)(level * 8388607 * Math.Sin(2 * Math.PI * 1000 * i / 48000));
                for (int ch = 0; ch < 2; ch++)
                {
                    s.WriteByte((byte)v);
                    s.WriteByte((byte)(v >> 8));
                    s.WriteByte((byte)(v >> 16));
                }
            }
        return s.ToArray();
    }
    static IReadOnlyList<AudioRange> Run(byte[] data, Options? o = null, int packet = 2880)
    {
        var d = new Detector(o ?? new());
        for (int i = 0; i < data.Length; i += packet)
            d.Push(data.AsSpan(i, Math.Min(packet, data.Length - i)));
        return d.Finish();
    }
    [Fact] public void ZeroDuration() => Assert.Empty(Run([]));
    [Fact] public void Silence() => Assert.Empty(Run(Pcm((3000, 0))));
    [Fact] public void Continuous() => Assert.Equal(new AudioRange(0, 48000), Assert.Single(Run(Pcm((1000, .2)))));
    [Fact] public void QuietNoise() => Assert.Empty(Run(Pcm((3000, .0001))));
    [Fact] public void PreAndPostRoll() => Assert.Equal(new AudioRange(38400, 105600), Assert.Single(Run(Pcm((1000, 0), (1000, .2), (2000, 0)))));
    [Fact] public void AbruptAttackAndEnd() => Assert.Equal(new AudioRange(0, 48000), Assert.Single(Run(Pcm((1000, .7)))));
    [Fact] public void SeparateActivity() => Assert.Equal(2, Run(Pcm((500, .2), (2000, 0), (500, .2))).Count);
    [Fact] public void ShortGapStaysInSequence() => Assert.Single(Run(Pcm((500, .2), (1000, 0), (500, .2))));
    [Fact]
    public void Hysteresis()
    {
        var data = Pcm((100, .02), (100, .005), (100, .02), (100, .005));
        Assert.Single(Run(data));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1024)]
    [InlineData(40000)]
    public void PacketBoundariesDoNotChangeOutput(int packet)
    {
        var data = Pcm((513, 0), (743, .3), (2127, 0), (123, .2));
        Assert.Equal(Run(data), Run(data, packet: packet));
    }
    [Fact]
    public void AllProcessingOffPreservesEveryFrame()
    {
        var data = Pcm((500, 0), (100, .2), (2000, 0));
        Assert.Equal(new AudioRange(0, 124800), Assert.Single(Run(data, new()
        {
            SkipSilence = false,
            TrimEdges = false
        })));
    }
    [Fact]
    public void EdgeTrimIndependentOfInternalSilence()
    {
        Assert.Equal(new AudioRange(14400, 187200), Assert.Single(Run(Pcm((500, 0), (100, .2), (3000, 0), (100, .2), (500, 0)), new()
        {
            SkipSilence = false
        })));
    }
    [Fact] public void BriefSoundPreservedByDefault() => Assert.Single(Run(Pcm((500, 0), (10, .2), (2000, 0))));
    [Fact] public void BriefEdgeRemovalOptIn() => Assert.Empty(Run(Pcm((500, 0), (10, .2), (2000, 0)), new() { RemoveBriefEdges = true }));
    [Fact] public void MinimumDurationInSeparateFiles() => Assert.Empty(Run(Pcm((500, 0), (10, .2), (2000, 0)), new() { SeparateFiles = true }));
    [Fact]
    public void NoDuplicateFramesWhenMarginsOverlap()
    {
        var o = new Options { SilenceMs = 200, PreRollMs = 500, PostRollMs = 500 };
        var r = Run(Pcm((500, .2), (300, 0), (500, .2)), o);
        Assert.Equal(new AudioRange(0, 62400), Assert.Single(r));
    }
    [Fact]
    public void StateTransitions()
    {
        var d = new Detector(new());
        d.Push(Pcm((100, 0)));
        Assert.False(d.Active);
        d.Push(Pcm((100, .2)));
        Assert.True(d.Active);
        d.Push(Pcm((1600, 0)));
        Assert.False(d.Active);
        Assert.Single(d.Finish());
    }
    [Fact] public void InvalidOptionsRejected() => Assert.Throws<ArgumentException>(() => new Detector(new() { SilenceMs = -1 }));
    [Fact]
    public void TruncatedFrameRejected()
    {
        var d = new Detector(new());
        d.Push(new byte[7]);
        Assert.Throws<InvalidDataException>(() => d.Finish());
    }
    [Fact]
    public async Task WavFlacRoundtripAndJoinsAreExact()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Sift-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var pcm = Pcm((500, 0), (1000, .2), (3000, 0), (500, .3), (500, 0));
            string raw = Path.Combine(dir, "música.pcm"), wav = Path.Combine(dir, "audio.wav"), flac = Path.Combine(dir, "audio.flac");
            File.WriteAllBytes(raw, pcm);
            var ranges = Run(pcm);
            Export.WriteWave(raw, wav, ranges);
            var expected = ranges.SelectMany(r => pcm.AsSpan((int)r.Start * 6, (int)(r.End - r.Start) * 6).ToArray()).ToArray();
            using (var reader = new WaveFileReader(wav))
            {
                Assert.Equal(24, reader.WaveFormat.BitsPerSample);
                var actual = new byte[reader.Length];
                reader.ReadExactly(actual);
                Assert.Equal(expected, actual);
            }
            await Export.EncodeFlac(wav, flac);
            var info = new System.Diagnostics.ProcessStartInfo(Export.FlacPath) { CreateNoWindow = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "--decode", "--silent", "--force-raw-format", "--endian=little", "--sign=signed", "-o", Path.Combine(dir, "decoded.pcm"), flac })
                info.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(info)!;
            var error = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            Assert.True(p.ExitCode == 0, await error);
            Assert.Equal(expected, File.ReadAllBytes(Path.Combine(dir, "decoded.pcm")));
            Assert.Throws<IOException>(() => Export.WriteWave(raw, wav, ranges));
            Assert.Throws<DirectoryNotFoundException>(() => Export.WriteWave(raw, Path.Combine(dir, "missing", "audio.wav"), ranges));
        }
        finally { Directory.Delete(dir, true); }
    }
}
