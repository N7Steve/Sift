using System.IO;
using System.Text.Json;
using Sift.Core;
namespace Sift;

public sealed class Preferences
{
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Sift");
    public string Format { get; set; } = "FLAC";
    public int BitrateKbps { get; set; } = 192;
    public Options Detection { get; set; } = new();
    public double Width { get; set; } = 680;
    public double Height { get; set; } = 860;
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    static string Config => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sift", "settings.json");
    public static Preferences Load()
    {
        try
        {
            var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Config)) ?? new();
            p.Detection.Validate();
            if (!AudioOutput.Formats.Contains(p.Format))
                p.Format = "FLAC";
            if (!AudioOutput.Bitrates.Contains(p.BitrateKbps))
                p.BitrateKbps = 192;
            return p;
        }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
        string temp = Config + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Config, true);
    }
}
