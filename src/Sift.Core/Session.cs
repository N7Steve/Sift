using System.Diagnostics;
using System.Text.Json;
namespace Sift.Core;

public sealed record RecordingResult(string Path, long Frames);
public sealed class Session : IAsyncDisposable
{
    readonly Capture capture;
    readonly int bitrateKbps;
    readonly Detector detector;
    readonly Options options;
    readonly string raw, format, stem;
    readonly Process target;
    readonly long identity;
    Task? worker;
    long estimatedFrames;
    public double SavedSeconds => Interlocked.Read(ref estimatedFrames) / 48000.0;
    readonly Stopwatch clock = new();
    public double LevelDb => detector.LevelDb;
    public bool Active => detector.Active;
    public double Seconds => clock.Elapsed.TotalSeconds;
    public double CapturedSeconds => detector.Frames / 48000.0;
    public string RecoveryPath => raw;
    public bool SourceExited
    {
        get
        {
            try
            {
                return target.HasExited || target.StartTime.Ticks != identity;
            }
            catch { return true; }
        }
    }
    public Exception? Error => capture.Failure ?? (worker?.Exception?.GetBaseException());
    Session(Capture c, Process p, Options o, string folder, string format, int bitrateKbps = 192)
    {
        capture = c;
        target = p;
        identity = p.StartTime.Ticks;
        options = o;
        detector = new(o);
        this.format = format;
        this.bitrateKbps = bitrateKbps;
        stem = Path.Combine(folder, $"Sift_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}");
        raw = stem + ".sift-pcm";
    }
    public static async Task<Session> Start(int pid, Options o, string folder, string format, int bitrateKbps = 192)
    {
        o.Validate();
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("La carpeta de destino no existe. Selecciona otra carpeta.");
        AudioOutput.Validate(format, bitrateKbps);
        if (format == "OGG")
            foreach (string dll in new[] { "ogg.dll", "vorbis.dll", "vorbisenc.dll" })
                if (!File.Exists(Path.Combine(AppContext.BaseDirectory, dll)))
                    throw new FileNotFoundException("Falta un componente OGG. Extrae el paquete portable completo.", dll);
        if (format == "MP3")
            AudioOutput.CheckMp3Support(bitrateKbps);
        if (format == "FLAC" && !File.Exists(Export.FlacPath))
            throw new FileNotFoundException("Falta el codificador FLAC. Ejecuta bootstrap.ps1.");
        var p = Process.GetProcessById(pid);
        Capture? c = null;
        Session? s = null;
        FileStream? file = null;
        try
        {
            c = await Capture.Open((uint)pid);
            s = new(c, p, o, folder, format, bitrateKbps);
            file = new FileStream(s.raw, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.SequentialScan);
            File.WriteAllText(s.raw + ".json", JsonSerializer.Serialize(new
            {
                SampleRate = 48000,
                Bits = 24,
                Channels = 2,
                Options = o,
                Format = format,
                BitrateKbps = AudioOutput.IsLossy(format) ? bitrateKbps : (int?)null,
                ProcessId = pid
            }));
            s.worker = Task.Run(async () => { using (file) { await foreach (var b in c.Packets.ReadAllAsync()) { file.Write(b); s.detector.Push(b); Interlocked.Exchange(ref s.estimatedFrames, s.detector.EstimatedSavedFrames); } file.Flush(true); } });
            c.Start();
            s.clock.Start();
            return s;
        }
        catch { if (s?.worker == null) file?.Dispose(); if (c != null) await c.DisposeAsync(); if (s?.worker != null) { try { await s.worker; } catch { } } p.Dispose(); throw; }
    }
    public async Task<IReadOnlyList<RecordingResult>> Stop()
    {
        await capture.Stop();
        clock.Stop();
        await worker!;
        var ranges = detector.Finish();
        if (Error != null)
            throw new IOException(Error.Message + " Recuperación: " + raw, Error);
        var groups = options.SeparateFiles ? ranges.Select(r => (IReadOnlyList<AudioRange>)new[] { r }).ToList() : new List<IReadOnlyList<AudioRange>> { ranges };
        var results = new List<RecordingResult>();
        int index = 0;
        foreach (var group in groups)
        {
            if (group.Count == 0)
                continue;
            string name = stem + (options.SeparateFiles ? $"_{++index:000}" : "");
            string wav = name + ".work.wav";
            if (!AudioOutput.IsLossy(format))
                Export.WriteWave(raw, wav, group);
            string output = name + "." + format.ToLowerInvariant();
            if (AudioOutput.IsLossy(format))
            {
                AudioOutput.Encode(raw, output + ".partial", group, format, bitrateKbps);
                File.Move(output + ".partial", output);
            }
            else if (format == "FLAC")
            {
                await Export.EncodeFlac(wav, output + ".partial");
                File.Move(output + ".partial", output);
                File.Delete(wav);
            }
            else
                File.Move(wav, output);
            results.Add(new(output, group.Sum(r => r.End - r.Start)));
        }
        // Recovery data is removed only after every requested output has finalized successfully.
        File.Delete(raw);
        File.Delete(raw + ".json");
        return results;
    }
    public async ValueTask DisposeAsync()
    {
        await capture.DisposeAsync();
        if (worker != null)
        {
            try
            {
                await worker;
            }
            catch { }
        }
        target.Dispose();
    }
}
