namespace Sift.Core;

public sealed record Options
{
    public bool SkipSilence { get; set; } = true;
    public bool TrimEdges { get; set; } = true;
    public bool RemoveBriefEdges
    {
        get; set;
    }
    public bool SeparateFiles
    {
        get; set;
    }
    public double ThresholdDb { get; set; } = -45;
    public int SilenceMs { get; set; } = 1500;
    public int PreRollMs { get; set; } = 200;
    public int PostRollMs { get; set; } = 200;
    public int MinimumMs { get; set; } = 80;
    public void Validate()
    {
        if (!double.IsFinite(ThresholdDb) || ThresholdDb is < -90 or > -6 || SilenceMs is < 200 or > 10000 || PreRollMs is < 0 or > 1000 || PostRollMs is < 0 or > 2000 || MinimumMs is < 0 or > 1000)
            throw new ArgumentException("Parámetros fuera del intervalo permitido.");
    }
}
public readonly record struct AudioRange(long Start, long End);
public sealed class Detector
{
    readonly Options options;
    readonly List<AudioRange> events = [];
    readonly byte[] window = new byte[480 * 6]; // fixed 10ms windows; independent of native packet boundaries
    int filled;
    long frames, start = -1, lastSound;
    public double LevelDb { get; private set; } = -100;
    public bool Active => start >= 0;
    public long EstimatedSavedFrames => Plan(Active ? events.Append(new AudioRange(start, lastSound)).ToArray() : events, Frames, options).Sum(r => r.End - r.Start);
    public long Frames => frames + filled / 6;
    public Detector(Options options)
    {
        options.Validate();
        this.options = options;
    }
    public void Push(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            int n = Math.Min(window.Length - filled, data.Length);
            data[..n].CopyTo(window.AsSpan(filled));
            filled += n;
            data = data[n..];
            if (filled == window.Length)
            {
                Analyze(window);
                filled = 0;
            }
        }
    }
    void Analyze(ReadOnlySpan<byte> data)
    {
        double sum = 0;
        for (int i = 0; i < data.Length; i += 3)
        {
            int v = (data[i] | data[i + 1] << 8 | data[i + 2] << 16);
            v = (v << 8) >> 8;
            double x = v / 8388608.0;
            sum += x * x;
        }
        LevelDb = 10 * Math.Log10(Math.Max(1e-20, sum / (data.Length / 3)));
        long end = frames + data.Length / 6;
        if (LevelDb >= options.ThresholdDb - (Active ? 6 : 0))
        {
            if (!Active)
                start = frames;
            lastSound = end;
        }
        else if (Active && end - lastSound >= options.SilenceMs * 48L)
        {
            events.Add(new(start, lastSound));
            start = -1;
        }
        frames = end;
    }
    public IReadOnlyList<AudioRange> Finish()
    {
        if (filled > 0)
        {
            if (filled % 6 != 0)
                throw new InvalidDataException("PCM incompleto.");
            Analyze(window.AsSpan(0, filled));
            filled = 0;
        }
        if (Active)
        {
            events.Add(new(start, lastSound));
            start = -1;
        }
        return Plan(events, frames, options);
    }
    public static IReadOnlyList<AudioRange> Plan(IReadOnlyList<AudioRange> source, long total, Options o)
    {
        var events = source.ToList();
        if (o.RemoveBriefEdges)
        {
            while (events.Count > 0 && events[0].End - events[0].Start < o.MinimumMs * 48L)
                events.RemoveAt(0);
            while (events.Count > 0 && events[^1].End - events[^1].Start < o.MinimumMs * 48L)
                events.RemoveAt(events.Count - 1);
        }
        if (total == 0)
            return [];
        if (!o.SkipSilence && !o.SeparateFiles)
        {
            if (!o.TrimEdges && !o.RemoveBriefEdges)
                return [new(0, total)];
            if (events.Count == 0)
                return [];
            return [new(Math.Max(0, events[0].Start - o.PreRollMs * 48L), Math.Min(total, events[^1].End + o.PostRollMs * 48L))];
        }
        var result = new List<AudioRange>();
        foreach (var e in events)
        {
            if (o.SeparateFiles && e.End - e.Start < o.MinimumMs * 48L)
                continue;
            var r = new AudioRange(Math.Max(0, e.Start - o.PreRollMs * 48L), Math.Min(total, e.End + o.PostRollMs * 48L));
            if (result.Count > 0 && r.Start <= result[^1].End)
                result[^1] = new(result[^1].Start, Math.Max(result[^1].End, r.End));
            else
                result.Add(r);
        }
        // Disabling edge trim preserves session boundaries independently of internal silence removal.
        if (!o.TrimEdges && !o.RemoveBriefEdges && result.Count > 0)
        {
            result[0] = new(0, result[0].End);
            result[^1] = new(result[^1].Start, total);
        }
        if (!o.TrimEdges && !o.RemoveBriefEdges && result.Count == 0 && !o.SeparateFiles)
            result.Add(new(0, total));
        return result;
    }
}
