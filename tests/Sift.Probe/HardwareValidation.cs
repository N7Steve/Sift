using System.Diagnostics;
using System.Numerics;
using NAudio.Wave;
using Sift.Core;
static class HardwareValidation
{
    public static async Task Run(string folder)
    {
        Directory.CreateDirectory(folder);
        Process Tone(int hz)
        {
            var info = new ProcessStartInfo("dotnet") { CreateNoWindow = true, UseShellExecute = false };
            info.ArgumentList.Add(typeof(HardwareValidation).Assembly.Location);
            info.ArgumentList.Add("tone");
            info.ArgumentList.Add(hz.ToString());
            return Process.Start(info)!;
        }
        using var selected = Tone(440);
        using var other = Tone(997);
        try
        {
            await Task.Delay(700);
            foreach (var format in new[] { "WAV", "FLAC", "MP3", "OGG" })
            {
                await using var session = await Session.Start(selected.Id, new()
                {
                    SeparateFiles = format == "FLAC"
                }, Path.GetFullPath(folder), format);
                await Task.Delay(1800);
                var files = await session.Stop();
                if (files.Count != 1)
                    throw new Exception("Expected one audio segment.");
                if (format == "WAV")
                {
                    using var reader = new WaveFileReader(files[0].Path);
                    byte[] b = new byte[reader.Length];
                    reader.ReadExactly(b);
                    double Amplitude(int hz)
                    {
                        Complex sum = 0;
                        int count = 0;
                        for (int i = 4800; i < Math.Min(b.Length / 6, 52800); i++)
                        {
                            int n = i * 6;
                            int v = (b[n] | b[n + 1] << 8 | b[n + 2] << 16);
                            v = (v << 8) >> 8;
                            sum += v / 8388608.0 * Complex.FromPolarCoordinates(1, -2 * Math.PI * hz * i / 48000);
                            count++;
                        }
                        return 2 * sum.Magnitude / count;
                    }
                    double wanted = Amplitude(440), unwanted = Amplitude(997);
                    Console.WriteLine($"WAV isolation: 440Hz={wanted:F8}, 997Hz={unwanted:F8}");
                    if (wanted < .01 || unwanted > wanted * .001)
                        throw new Exception("Isolation test failed (60dB rejection required).");
                }
                Console.WriteLine($"PASS {format}: {files[0].Frames} frames, {files[0].Path}");
            }
            other.Kill();
            await other.WaitForExitAsync();
            await using var ending = await Session.Start(selected.Id, new(), Path.GetFullPath(folder), "WAV");
            await Task.Delay(400);
            selected.Kill();
            await selected.WaitForExitAsync();
            if (!ending.SourceExited)
                throw new Exception("Source exit not detected.");
            var final = await ending.Stop();
            if (final.Count != 1)
                throw new Exception("Partial capture was not preserved.");
            Console.WriteLine("PASS source exit, repeated start/stop and native disposal.");
            var idleInfo = new ProcessStartInfo("dotnet") { CreateNoWindow = true, UseShellExecute = false };
            idleInfo.ArgumentList.Add(typeof(HardwareValidation).Assembly.Location);
            idleInfo.ArgumentList.Add("idle");
            using var idle = Process.Start(idleInfo)!;
            try
            {
                await using var silent = await Session.Start(idle.Id, new(), Path.GetFullPath(folder), "WAV");
                await Task.Delay(500);
                var silentFiles = await silent.Stop();
                if (silentFiles.Count != 0)
                    throw new Exception("Silent process produced unexpected retained audio.");
                Console.WriteLine("PASS real silent process produces no retained file.");
            }
            finally { if (!idle.HasExited) idle.Kill(); }

        }
        finally { if (!selected.HasExited) selected.Kill(); if (!other.HasExited) other.Kill(); }
    }
}
