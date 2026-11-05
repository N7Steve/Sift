using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
namespace Sift;

public sealed record ApplicationItem(int Pid, string Name, string Context, ImageSource? Icon, string Activity)
{
    public override string ToString() => Name;
}
public static class Applications
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Display; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attributes, out SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    public static List<ApplicationItem> List()
    {
        var audio = new Dictionary<int, bool>();
        try
        {
            using var e = new MMDeviceEnumerator();
            using var devices = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var s = sessions[i];
                        int pid = (int)s.GetProcessID;
                        audio[pid] = audio.GetValueOrDefault(pid) || s.AudioMeterInformation.MasterPeakValue > 0.0001f;
                    }
                }
            }
        }
        catch { }
        var list = new List<ApplicationItem>();
        foreach (var p in Process.GetProcesses())
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId || (p.MainWindowHandle == IntPtr.Zero && !audio.ContainsKey(p.Id)))
                        continue;
                    string path = p.MainModule?.FileName ?? "";
                    string name = path.Length > 0 ? FileVersionInfo.GetVersionInfo(path).FileDescription ?? p.ProcessName : p.ProcessName;
                    if (string.IsNullOrWhiteSpace(name))
                        name = p.ProcessName;
                    ImageSource? icon = null;
                    if (path.Length > 0 && SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 | 1) != IntPtr.Zero && info.Icon != IntPtr.Zero)
                    {
                        try
                        {
                            icon = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            icon.Freeze();
                        }
                        finally { DestroyIcon(info.Icon); }
                    }
                    list.Add(new(p.Id, name, string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.ProcessName : p.MainWindowTitle, icon, audio.GetValueOrDefault(p.Id) ? "● Audio activo" : ""));
                }
                catch { }
            }
        return list.OrderByDescending(x => x.Activity.Length).ThenBy(x => x.Name).ToList();
    }
}
