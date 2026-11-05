using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Sift.Core;
namespace Sift;

public partial class MainWindow : Window
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    readonly Preferences prefs = Preferences.Load();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    Session? session;
    bool busy, closing;
    string? last;
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => { int dark = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, 4); };
        Width = Math.Clamp(prefs.Width, 540, Math.Max(540, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(prefs.Height, 650, Math.Max(650, SystemParameters.WorkArea.Height));
        if (double.IsFinite(prefs.Left) && double.IsFinite(prefs.Top) && prefs.Left >= 0 && prefs.Top >= 0 && prefs.Left + Width <= SystemParameters.VirtualScreenWidth && prefs.Top + Height <= SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = prefs.Left;
            Top = prefs.Top;
        }
        Folder.Text = prefs.Folder;
        Format.SelectedIndex = prefs.Format == "WAV" ? 1 : 0;
        var o = prefs.Detection;
        Skip.IsChecked = o.SkipSilence;
        Trim.IsChecked = o.TrimEdges;
        Brief.IsChecked = o.RemoveBriefEdges;
        Mode.SelectedIndex = o.SeparateFiles ? 1 : 0;
        Threshold.Text = o.ThresholdDb.ToString(CultureInfo.InvariantCulture);
        Silence.Text = o.SilenceMs.ToString();
        Pre.Text = o.PreRollMs.ToString();
        Post.Text = o.PostRollMs.ToString();
        Minimum.Text = o.MinimumMs.ToString();
        timer.Tick += Tick;
        timer.Start();
        Loaded += async (_, _) => await RefreshApps();
    }
    async Task RefreshApps()
    {
        Refresh.IsEnabled = false;
        try
        {
            int? pid = (Apps.SelectedItem as ApplicationItem)?.Pid;
            var items = await Task.Run(Applications.List);
            Apps.ItemsSource = items;
            Apps.SelectedItem = items.FirstOrDefault(x => x.Pid == pid) ?? items.FirstOrDefault();
        }
        catch (Exception e) { Message.Text = e.Message; }
        finally { Refresh.IsEnabled = true; }
    }
    async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshApps();
    void BrowseClick(object sender, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog { Title = "Carpeta de grabaciones" };
        if (Directory.Exists(Folder.Text))
            d.InitialDirectory = Folder.Text;
        if (d.ShowDialog() == true)
            Folder.Text = d.FolderName;
    }
    Options ReadOptions() => new() { SkipSilence = Skip.IsChecked == true, TrimEdges = Trim.IsChecked == true, RemoveBriefEdges = Brief.IsChecked == true, SeparateFiles = Mode.SelectedIndex == 1, ThresholdDb = double.Parse(Threshold.Text, CultureInfo.InvariantCulture), SilenceMs = int.Parse(Silence.Text), PreRollMs = int.Parse(Pre.Text), PostRollMs = int.Parse(Post.Text), MinimumMs = int.Parse(Minimum.Text) };
    void Lock(bool value)
    {
        Apps.IsEnabled = !value;
        Refresh.IsEnabled = !value;
        OutputControls.IsEnabled = !value;
        DetectionControls.IsEnabled = !value;
    }
    async void RecordClick(object sender, RoutedEventArgs e)
    {
        if (busy)
            return;
        if (session != null)
        {
            await Stop();
            return;
        }
        busy = true;
        Record.IsEnabled = false;
        try
        {
            if (Apps.SelectedItem is not ApplicationItem app)
                throw new InvalidOperationException("Selecciona una aplicación.");
            var o = ReadOptions();
            o.Validate();
            if (Folder.Text == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Sift"))
                Directory.CreateDirectory(Folder.Text);
            session = await Session.Start(app.Pid, o, Folder.Text, ((ComboBoxItem)Format.SelectedItem).Content.ToString()!);
            Lock(true);
            SavedLabel.Text = "AUDIO GUARDADO ≈";
            Record.Content = "■  Detener";
            Message.Text = "Captura por aplicación activa. Los recortes se aplican al detener.";
        }
        catch (Exception ex) { Message.Text = "No se pudo iniciar: " + ex.Message; }
        finally { busy = false; Record.IsEnabled = true; }
    }
    async Task Stop()
    {
        if (session == null || busy)
            return;
        busy = true;
        Record.IsEnabled = false;
        Status.Text = "Finalizando";
        var s = session;
        try
        {
            var results = await Task.Run(s.Stop);
            if (results.Count > 0)
            {
                last = results[^1].Path;
                ResultPanel.Visibility = Visibility.Visible;
                long bytes = results.Sum(r => new FileInfo(r.Path).Length);
                double seconds = results.Sum(r => r.Frames) / 48000.0;
                SavedLabel.Text = "AUDIO GUARDADO";
                Saved.Text = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
                ResultText.Text = $"{Path.GetFileName(last)}\n{results.Count} archivo(s) · {seconds:F2} s · {bytes / 1048576.0:F2} MB";
                Message.Text = s.SourceExited ? "La aplicación terminó. Se ha guardado el audio capturado." : "Grabación guardada.";
            }
            else
                Message.Text = "No se detectó audio que conservar con estas opciones.";
        }
        catch (Exception ex) { Message.Text = "Grabación interrumpida: " + ex.Message + "\nPCM recuperable: " + s.RecoveryPath; }
        finally { await s.DisposeAsync(); session = null; busy = false; Lock(false); Record.IsEnabled = true; Record.Content = "●  Grabar"; Status.Text = "Listo"; Meter.Value = 0; Signal.Text = "Captura detenida"; LevelText.Text = "−∞ dBFS"; }
    }
    async void Tick(object? sender, EventArgs e)
    {
        if (session == null || busy)
            return;
        Status.Text = session.Active ? "Grabando" : "Esperando sonido";
        Signal.Text = session.Active ? "● Señal detectada" : "Captura activa · silencio";
        Meter.Value = Math.Clamp((session.LevelDb + 70) / 70 * 100, 0, 100);
        LevelText.Text = $"{session.LevelDb:F1} dBFS";
        Elapsed.Text = TimeSpan.FromSeconds(session.Seconds).ToString(@"hh\:mm\:ss");
        Saved.Text = TimeSpan.FromSeconds(session.SavedSeconds).ToString(@"hh\:mm\:ss");
        if (session.SourceExited || session.Error != null)
            await Stop();
    }
    void PlayClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (last != null)
                Process.Start(new ProcessStartInfo(last) { UseShellExecute = true });
        }
        catch (Exception ex) { Message.Text = ex.Message; }
    }
    void OpenClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = new ProcessStartInfo("explorer.exe");
            info.ArgumentList.Add(last != null ? Path.GetDirectoryName(last)! : Folder.Text);
            Process.Start(info);
        }
        catch (Exception ex) { Message.Text = ex.Message; }
    }
    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing)
            return;
        e.Cancel = true;
        if (busy)
            return;
        if (session != null)
            await Stop();
        try
        {
            prefs.Folder = Folder.Text;
            prefs.Format = ((ComboBoxItem)Format.SelectedItem).Content.ToString()!;
            prefs.Detection = ReadOptions();
            prefs.Detection.Validate();
            prefs.Width = RestoreBounds.Width;
            prefs.Height = RestoreBounds.Height;
            prefs.Left = RestoreBounds.Left;
            prefs.Top = RestoreBounds.Top;
            prefs.Save();
        }
        catch { }
        closing = true;
        timer.Stop();
        Close();
    }
}
