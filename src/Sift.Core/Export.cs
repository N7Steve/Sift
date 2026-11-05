using System.Diagnostics;
using System.Text;
namespace Sift.Core;

public static class Export
{
    public static string FlacPath => Path.Combine(AppContext.BaseDirectory, "codec", "flac.exe");
    // RIFF with reserved ds64 chunk; automatically becomes RF64 above 4 GiB.
    public static void WriteWave(string raw, string output, IReadOnlyList<AudioRange> ranges)
    {
        long size = checked(ranges.Sum(r => r.End - r.Start) * 6);
        using var input = File.OpenRead(raw);
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var w = new BinaryWriter(file, Encoding.ASCII, true);
        bool large = size + 72 > uint.MaxValue;
        void Tag(string t) => w.Write(Encoding.ASCII.GetBytes(t));
        Tag(large ? "RF64" : "RIFF");
        w.Write(large ? uint.MaxValue : (uint)(size + 72));
        Tag("WAVE");
        Tag(large ? "ds64" : "JUNK");
        w.Write(28);
        w.Write((ulong)(size + 72));
        w.Write((ulong)size);
        w.Write((ulong)(size / 6));
        w.Write(0);
        Tag("fmt ");
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(48000);
        w.Write(288000);
        w.Write((short)6);
        w.Write((short)24);
        Tag("data");
        w.Write(large ? uint.MaxValue : (uint)size);
        byte[] buffer = new byte[1024 * 1024];
        foreach (var r in ranges)
        {
            input.Position = r.Start * 6;
            long remaining = (r.End - r.Start) * 6;
            while (remaining > 0)
            {
                int n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (n == 0)
                    throw new EndOfStreamException();
                file.Write(buffer, 0, n);
                remaining -= n;
            }
        }
        file.Flush(true);
    }
    public static async Task EncodeFlac(string wav, string output)
    {
        var info = new ProcessStartInfo(FlacPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "--silent", "--verify", "--no-padding", "--tag=ENCODER=Sift", "-o", output, wav })
            info.ArgumentList.Add(arg);
        using var p = Process.Start(info) ?? throw new IOException("No se pudo iniciar FLAC.");
        var errors = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        await stdout;
        if (p.ExitCode != 0)
            throw new IOException("FLAC: " + await errors);
    }
}
