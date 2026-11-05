using Sift.Core;
using NAudio.Wave;
if (args.Length > 0 && args[0] == "idle") { await Task.Delay(15000); return; }
if (args.Length == 3 && args[0] == "recover") { long frames = new FileInfo(args[1]).Length / 6; Export.WriteWave(args[1], args[2], [new AudioRange(0, frames)]); Console.WriteLine("Recovered without modifying source PCM."); return; }
if (args.Length > 0 && args[0] == "validate") { await HardwareValidation.Run(args.Length > 1 ? args[1] : "artifacts/validation"); return; }
if (args.Length > 0 && args[0] == "tone")
{
    using var output = new WaveOut();
    output.Init(new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = double.Parse(args[1]), Gain = 0.15, Type = NAudio.Wave.SampleProviders.SignalGeneratorType.Sin });
    output.Play();
    await Task.Delay(15000);
    return;
}
if (args.Length < 3) { Console.WriteLine("Sift.Probe <pid> <seconds> <output.wav>"); return; }
await using var capture = await Capture.Open(uint.Parse(args[0]));
Console.WriteLine(capture.Format);
using var writer = new WaveFileWriter(args[2], capture.Format);
var consume = Task.Run(async () => { await foreach (var p in capture.Packets.ReadAllAsync()) writer.Write(p, 0, p.Length); });
capture.Start();
await Task.Delay(TimeSpan.FromSeconds(double.Parse(args[1])));
await capture.Stop();
await consume;
if (capture.Failure != null) throw capture.Failure;
Console.WriteLine($"Saved {writer.Length} bytes");
