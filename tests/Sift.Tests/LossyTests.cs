using NAudio.Wave;
using NVorbis;
using Sift.Core;

namespace Sift.Tests;

public class LossyTests
{
    public static IEnumerable<object[]> Rates => AudioOutput.Bitrates.SelectMany(rate => new[] { new object[] { "MP3", rate }, new object[] { "OGG", rate } });

    [Theory]
    [MemberData(nameof(Rates))]
    public void EveryBitrateProducesDecodableStereoAudio(string format, int rate)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Sift-lossy-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string raw = Path.Combine(folder, "música.pcm"), output = Path.Combine(folder, "música." + format.ToLowerInvariant());
            // Two retained 500 ms intervals; the middle 500 ms must never reach the encoder.
            using (var f = File.Create(raw))
                for (int i = 0; i < 72000; i++)
                    for (int channel = 0; channel < 2; channel++)
                    {
                        double frequency = i is >= 24000 and < 48000 ? 997 : 1000;
                        int value = (int)(Math.Sin(i * 2 * Math.PI * frequency / 48000) * 0.2 * 8388607);
                        f.WriteByte((byte)value);
                        f.WriteByte((byte)(value >> 8));
                        f.WriteByte((byte)(value >> 16));
                    }
            AudioRange[] ranges = [new(0, 24000), new(48000, 72000)];
            AudioOutput.Encode(raw, output, ranges, format, rate);
            var samples = new List<float>();
            if (format == "OGG")
            {
                using var reader = new VorbisReader(output);
                Assert.Equal(2, reader.Channels);
                Assert.Equal(48000, reader.SampleRate);
                Assert.InRange(reader.NominalBitrate, rate * 1000 - 1000, rate * 1000 + 1000);
                float[] buffer = new float[4096];
                int n;
                while ((n = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                    samples.AddRange(buffer.Take(n));
                Assert.Equal(96000, samples.Count); // exact Ogg granule duration, including final packet flush
            }
            else
            {
                using (var file = File.OpenRead(output))
                {
                    int frames = 0;
                    Mp3Frame? frame;
                    while ((frame = Mp3Frame.LoadFromStream(file)) != null)
                    {
                        Assert.Equal(rate * 1000, frame.BitRate);
                        Assert.Equal(48000, frame.SampleRate);
                        frames++;
                    }
                    Assert.True(frames > 20, $"Only {frames} MP3 frames");
                }
                using var reader = new MediaFoundationReader(output);
                Assert.Equal(2, reader.WaveFormat.Channels);
                Assert.Equal(48000, reader.WaveFormat.SampleRate);
                var provider = reader.ToSampleProvider();
                float[] buffer = new float[4096];
                int n;
                while ((n = provider.Read(buffer)) > 0)
                    samples.AddRange(buffer.Take(n));
                // Windows MP3 may expose codec delay/padding rather than gapless duration.
                Assert.InRange(samples.Count, 96000, 96000 + 4 * 1152);
            }
            Assert.All(samples, x => Assert.True(float.IsFinite(x)));
            Assert.InRange(Math.Sqrt(samples.Sum(x => (double)x * x) / samples.Count), .1, .2);
            // Check the retained tone and the absence of the deliberately omitted interval's tone.
            double Magnitude(int hz)
            {
                System.Numerics.Complex sum = 0;
                for (int i = 0; i < 24000; i++)
                    sum += samples[4800 + 2 * i] * System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI * hz * i / 48000);
                return sum.Magnitude / 24000;
            }
            Assert.True(Magnitude(1000) > Magnitude(997) * 3, $"Unexpected spectrum: {Magnitude(1000)}, {Magnitude(997)}");
            var before = File.ReadAllBytes(output);
            Assert.Throws<IOException>(() => AudioOutput.Encode(raw, output, ranges, format, rate));
            Assert.Equal(before, File.ReadAllBytes(output));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("MP3")]
    [InlineData("OGG")]
    public void VeryShortSilentSequenceFinalizes(string format)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Sift-short-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string raw = Path.Combine(folder, "audio.pcm"), output = Path.Combine(folder, "audio." + format.ToLowerInvariant());
            File.WriteAllBytes(raw, new byte[480 * 6]);
            if (format == "MP3")
            {
                Assert.Throws<IOException>(() => AudioOutput.Encode(raw, output, [new(0, 480)], format, 192));
                Assert.False(File.Exists(output));
                Assert.True(File.Exists(raw));
                return;
            }
            AudioOutput.Encode(raw, output, [new(0, 480)], format, 192);
            Assert.True(new FileInfo(output).Length > 0);
            if (format == "OGG")
            {
                using var reader = new VorbisReader(output);
                Assert.Equal(480, reader.TotalSamples);
                var samples = new float[960];
                Assert.Equal(960, reader.ReadSamples(samples, 0, samples.Length));
            }
            else
            {
                using var reader = new MediaFoundationReader(output);
                Assert.True(reader.Read(new byte[4096]) > 0);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(96)]
    [InlineData(128)]
    [InlineData(160)]
    [InlineData(192)]
    [InlineData(256)]
    [InlineData(320)]
    public void MinimumMp3DurationIsPlayable(int rate)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Sift-minimum-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string raw = Path.Combine(folder, "audio.pcm"), output = Path.Combine(folder, "audio.mp3");
            File.WriteAllBytes(raw, new byte[4800 * 6]);
            AudioOutput.Encode(raw, output, [new(0, 4800)], "MP3", rate);
            using var reader = new MediaFoundationReader(output);
            Assert.True(reader.Read(new byte[4096]) > 0);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("MP3", 0)]
    [InlineData("OGG", 999)]
    [InlineData("BAD", 192)]
    public void InvalidEncodingRejected(string format, int rate) => Assert.Throws<ArgumentException>(() => AudioOutput.Validate(format, rate));
}
