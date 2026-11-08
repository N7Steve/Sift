using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;
using OggVorbisSharp;

namespace Sift.Core;

public static class AudioOutput
{
    public static IReadOnlyList<string> Formats { get; } = Array.AsReadOnly(new[] { "FLAC", "WAV", "MP3", "OGG" });
    public static IReadOnlyList<int> Bitrates { get; } = Array.AsReadOnly(new[] { 96, 128, 160, 192, 256, 320 });
    public static bool IsLossy(string format) => format is "MP3" or "OGG";

    public static void Validate(string format, int bitrateKbps)
    {
        if (!Formats.Contains(format))
            throw new ArgumentException("Formato desconocido.");
        if (IsLossy(format) && !Bitrates.Contains(bitrateKbps))
            throw new ArgumentException("Selecciona un bitrate de 96, 128, 160, 192, 256 o 320 kbps.");
    }

    public static void CheckMp3Support(int bitrateKbps)
    {
        try
        {
            if (!MediaFoundationEncoder.GetEncodeBitrates(AudioSubtypes.MFAudioFormat_MP3, 48000, 2).Contains(bitrateKbps * 1000))
                throw new NotSupportedException("El codificador MP3 de Windows no admite el bitrate seleccionado.");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or DllNotFoundException)
        {
            throw new NotSupportedException("MP3 requiere Media Foundation, que no está disponible en esta edición de Windows. Puedes usar OGG, FLAC o WAV.", ex);
        }
    }

    public static void Encode(string raw, string output, IReadOnlyList<AudioRange> ranges, string format, int bitrateKbps)
    {
        Validate(format, bitrateKbps);
        if (!IsLossy(format))
            throw new ArgumentException("Este codificador admite MP3 y OGG.");
        if (format == "MP3")
        {
            CheckMp3Support(bitrateKbps);
            if (ranges.Sum(r => r.End - r.Start) < 4800)
                throw new IOException("MP3 necesita al menos 100 ms de audio para una reproducción compatible. Usa OGG, FLAC o WAV para sonidos más breves. El PCM se conserva.");
        }
        using var pcm = new RetainedPcm(raw, ranges);
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (format == "MP3")
        {
            // Windows MP3 accepts PCM16. Only this lossy export quantizes PCM24;
            // capture and the lossless formats retain their original bytes.
            var pcm16 = new SampleToWaveProvider16(pcm.ToSampleProvider());
            MediaFoundationEncoder.EncodeToMp3(pcm16, file, bitrateKbps * 1000);
        }
        else
            EncodeVorbis(pcm, file, bitrateKbps);
        file.Flush(true);
    }

    [DllImport("vorbisenc", CallingConvention = CallingConvention.Cdecl)]
    static extern unsafe int vorbis_encode_init(vorbis_info* info, int channels, int sampleRate, int maxRate, int nominalRate, int minRate);

    static void Check(int result)
    {
        if (result != 0)
            throw new IOException($"El codificador OGG falló ({result}). El PCM original se conserva.");
    }

    static unsafe void Pages(ogg_stream_state* ogg, Stream output, bool force)
    {
        ogg_page page = default;
        int result;
        while ((result = force ? Ogg.ogg_stream_flush(ogg, &page) : Ogg.ogg_stream_pageout(ogg, &page)) != 0)
        {
            if (result < 0)
                throw new IOException("Error al generar páginas OGG.");
            output.Write(new ReadOnlySpan<byte>(page.header, checked((int)page.header_len.Value)));
            output.Write(new ReadOnlySpan<byte>(page.body, checked((int)page.body_len.Value)));
        }
    }

    static unsafe void Drain(vorbis_dsp_state* dsp, vorbis_block* block, ogg_stream_state* ogg, Stream output)
    {
        int result;
        while ((result = Vorbis.vorbis_analysis_blockout(dsp, block)) == 1)
        {
            Check(Vorbis.vorbis_analysis(block, null));
            Check(Vorbis.vorbis_bitrate_addblock(block));
            ogg_packet packet = default;
            int packetResult;
            while ((packetResult = Vorbis.vorbis_bitrate_flushpacket(dsp, &packet)) == 1)
            {
                Check(Ogg.ogg_stream_packetin(ogg, &packet));
                Pages(ogg, output, false);
            }
            if (packetResult < 0)
                throw new IOException("Error al generar paquetes Vorbis.");
        }
        if (result < 0)
            throw new IOException("Error de análisis Vorbis.");
    }

    static unsafe void EncodeVorbis(IWaveProvider pcm, Stream output, int bitrateKbps)
    {
        vorbis_info info = default;
        vorbis_comment comments = default;
        vorbis_dsp_state dsp = default;
        vorbis_block block = default;
        ogg_stream_state ogg = default;
        bool dspReady = false, blockReady = false, oggReady = false;
        Vorbis.vorbis_info_init(&info);
        Vorbis.vorbis_comment_init(&comments);
        try
        {
            // Rate-managed Vorbis: nominal average target, without hard upper/lower limits.
            Check(vorbis_encode_init(&info, 2, 48000, -1, bitrateKbps * 1000, -1));
            Check(Vorbis.vorbis_analysis_init(&dsp, &info));
            dspReady = true;
            Check(Vorbis.vorbis_block_init(&dsp, &block));
            blockReady = true;
            Check(Ogg.ogg_stream_init(&ogg, Random.Shared.Next()));
            oggReady = true;
            byte[] tag = "ENCODER\0"u8.ToArray(), value = "Sift\0"u8.ToArray();
            fixed (byte* t = tag, v = value)
                Vorbis.vorbis_comment_add_tag(&comments, t, v);
            ogg_packet identification = default, comment = default, setup = default;
            Check(Vorbis.vorbis_analysis_headerout(&dsp, &comments, &identification, &comment, &setup));
            Check(Ogg.ogg_stream_packetin(&ogg, &identification));
            Check(Ogg.ogg_stream_packetin(&ogg, &comment));
            Check(Ogg.ogg_stream_packetin(&ogg, &setup));
            Pages(&ogg, output, true);
            var samples = pcm.ToSampleProvider();
            var interleaved = new float[4096 * 2];
            int count;
            while ((count = samples.Read(interleaved)) > 0)
            {
                if (count % 2 != 0)
                    throw new InvalidDataException("PCM estéreo incompleto.");
                float** target = Vorbis.vorbis_analysis_buffer(&dsp, count / 2);
                if (target == null)
                    throw new OutOfMemoryException("No se pudo reservar el búfer Vorbis.");
                for (int i = 0; i < count / 2; i++)
                {
                    target[0][i] = interleaved[i * 2];
                    target[1][i] = interleaved[i * 2 + 1];
                }
                Check(Vorbis.vorbis_analysis_wrote(&dsp, count / 2));
                Drain(&dsp, &block, &ogg, output);
            }
            Check(Vorbis.vorbis_analysis_wrote(&dsp, 0));
            Drain(&dsp, &block, &ogg, output);
            Pages(&ogg, output, true);
        }
        finally
        {
            if (oggReady)
                Ogg.ogg_stream_clear(&ogg);
            if (blockReady)
                Vorbis.vorbis_block_clear(&block);
            if (dspReady)
                Vorbis.vorbis_dsp_clear(&dsp);
            Vorbis.vorbis_comment_clear(&comments);
            Vorbis.vorbis_info_clear(&info);
        }
    }

    // Stream retained intervals directly, with bounded buffers and 64-bit offsets.
    // No RIFF size limit and no extra intermediate WAV for lossy exports.
    sealed class RetainedPcm : IWaveProvider, IDisposable
    {
        readonly FileStream file;
        readonly IReadOnlyList<AudioRange> ranges;
        int index;
        long offset;
        public WaveFormat WaveFormat { get; } = new(48000, 24, 2);
        public RetainedPcm(string raw, IReadOnlyList<AudioRange> ranges)
        {
            file = File.OpenRead(raw);
            this.ranges = ranges;
        }
        public int Read(Span<byte> buffer)
        {
            int written = 0;
            int count = buffer.Length;
            while (written < count && index < ranges.Count)
            {
                var range = ranges[index];
                long remaining = checked((range.End - range.Start) * 6) - offset;
                if (remaining <= 0)
                {
                    index++;
                    offset = 0;
                    continue;
                }
                file.Position = checked(range.Start * 6 + offset);
                int n = (int)Math.Min(count - written, remaining);
                file.ReadExactly(buffer.Slice(written, n));
                written += n;
                offset += n;
            }
            return written;
        }
        public void Dispose() => file.Dispose();
    }
}

