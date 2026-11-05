using System.Threading.Channels;
using NAudio.Wave;
using NAudio.CoreAudioApi;
namespace Sift.Core;

// Native spans are copied before the callback returns. The consumer owns each copy.
public sealed class Capture : IAsyncDisposable
{
    readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    WasapiRecorder? recorder;
    volatile Exception? failure;
    public WaveFormat Format => recorder!.WaveFormat;
    public ChannelReader<byte[]> Packets => queue.Reader;
    public Exception? Failure => failure;
    public static async Task<Capture> Open(uint pid)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new PlatformNotSupportedException("Sift requiere Windows 11.");
        if (pid == 0)
            throw new ArgumentOutOfRangeException(nameof(pid), "Selecciona un proceso de aplicación válido.");
        var c = new Capture();
        c.recorder = await new WasapiRecorderBuilder().WithProcessLoopback(pid)
            .WithFormat(new WaveFormat(48000, 24, 2)).WithBufferLength(40).WithMmcssThreadPriority().BuildAsync();
        if (c.Format.SampleRate != 48000 || c.Format.Channels != 2 || c.Format.BitsPerSample != 24 || c.Format.Encoding != WaveFormatEncoding.Pcm)
        {
            await c.DisposeAsync();
            throw new NotSupportedException("Windows no entregó el formato PCM24 estéreo de 48 kHz solicitado.");
        }
        c.recorder.DataAvailable += (data, flags, position, qpc) =>
        {
            if (c.failure != null)
                return;
            if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0 && position != 0)
                c.failure = new IOException("Windows informó una discontinuidad de audio. La captura parcial se conserva.");
            else if (!c.queue.Writer.TryWrite(data.ToArray()))
                c.failure = new IOException("El disco no sigue el ritmo de captura; búfer saturado. La captura parcial se conserva.");
            if (c.failure != null)
                c.queue.Writer.TryComplete();
        };
        c.recorder.RecordingStopped += (_, e) => { c.failure ??= e.Exception; c.queue.Writer.TryComplete(); c.stopped.TrySetResult(); };
        return c;
    }
    public void Start() => recorder!.StartRecording();
    public async Task Stop()
    {
        recorder!.StopRecording();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    public async ValueTask DisposeAsync()
    {
        if (recorder != null)
        {
            await recorder.DisposeAsync();
            recorder = null;
        }
        queue.Writer.TryComplete();
    }
}
