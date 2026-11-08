param([ValidateSet("FLAC","WAV","MP3","OGG")][string]$Format="FLAC", [ValidateSet(96,128,160,192,256,320)][int]$Bitrate=192)
# Interactive Windows test: plays a short tone and exercises the real WPF buttons.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class SiftScreen {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
}
"@
$folder = Join-Path $repo ('artifacts/validation/ui-' + $Format.ToLowerInvariant())
New-Item -ItemType Directory -Force $folder | Out-Null
$config = Join-Path $env:LOCALAPPDATA 'Sift/settings.json'
$previous = if (Test-Path $config) { [IO.File]::ReadAllText($config) } else { $null }
$settings = if ($previous) { $previous | ConvertFrom-Json } else { [PSCustomObject]@{ Format='FLAC'; Folder=$folder } }
$settings.Format=$Format
$settings | Add-Member -NotePropertyName BitrateKbps -NotePropertyValue $Bitrate -Force
[IO.Directory]::CreateDirectory((Split-Path $config)) | Out-Null
[IO.File]::WriteAllText($config,($settings | ConvertTo-Json -Depth 10))
$tone = Start-Process dotnet -ArgumentList 'tests/Sift.Probe/bin/Release/net10.0-windows10.0.22000.0/Sift.Probe.dll tone 440' -WindowStyle Hidden -PassThru
$app = Start-Process (Join-Path $repo 'artifacts/Sift-win-x64/Sift.exe') -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 2
    $root = [System.Windows.Automation.AutomationElement]::FromHandle((Get-Process -Id $app.Id).MainWindowHandle)
    function Find($id) { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,[string]$id)) }
    (Find 'Refresh').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 1
    $combo = Find 'Apps'
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,[string]$tone.Id))
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
    if ($null -eq $item) { throw 'Test tone process was not discovered.' }
    # WPF's realized container exposes no SelectionItem provider; exercise keyboard navigation.
    $combo.SetFocus()
    $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
    $index = -1
    for ($i=0; $i -lt $items.Count; $i++) { if ($items[$i].Current.AutomationId -eq [string]$tone.Id) { $index=$i; break } }
    if ($index -lt 0) { throw 'Cannot locate test source index.' }
    $keys = '{HOME}' + ('{DOWN}' * $index) + '{ENTER}'
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    (Find 'Folder').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($folder)
    (Find 'Record').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    if ((Find 'Status').Current.Name -ne 'Grabando') { throw ('UI capture did not become active: ' + (Find 'Message').Current.Name) }
    (Find 'Record').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    if ((Find 'Message').Current.Name -ne 'Grabación guardada.') { throw ('UI finalization failed: ' + (Find 'Message').Current.Name) }
    if (-not (Get-ChildItem $folder -Filter ('*.' + $Format.ToLowerInvariant()))) { throw 'No output from UI.' }
    if ($Format -in @('MP3','OGG') -and $null -eq (Find 'Bitrate')) { throw 'Bitrate selector is missing.' }
    $rect = New-Object SiftScreen+Rect
    [SiftScreen]::GetWindowRect($app.MainWindowHandle,[ref]$rect) | Out-Null
    $bitmap = New-Object Drawing.Bitmap(($rect.R-$rect.L),($rect.B-$rect.T))
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    [SiftScreen]::PrintWindow($app.MainWindowHandle,$dc,2) | Out-Null
    $graphics.ReleaseHdc($dc)
    $bitmap.Save((Join-Path $repo 'artifacts/validation/ui.png'))
    $graphics.Dispose(); $bitmap.Dispose()
    Write-Host "PASS portable UI: $Format at $Bitrate kbps, capture and finalized output."
} finally {
    if (-not $app.HasExited) { $app.CloseMainWindow() | Out-Null; $app.WaitForExit(15000) | Out-Null }
    if (-not $tone.HasExited) { $tone.Kill() }
    if ($null -ne $previous) { [IO.File]::WriteAllText($config,$previous) }
}
