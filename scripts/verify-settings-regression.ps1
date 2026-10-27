# 设置页回归自检：改一个下拉、拨一个开关，确认其它控件选中项/尺寸不被重置。
# 用法：pwsh -File scripts/verify-settings-regression.ps1
# 依赖：Windows UI Automation（Windows PowerShell 5.1 / PowerShell 7 均可）。
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$rootDir = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $rootDir 'src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe'
$shotDir = Join-Path $rootDir '.scratch\rework'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

if (-not (Test-Path $exe)) { "NO_EXE $exe"; exit 1 }
Get-Process PdxLauncher.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 10
if ($p.HasExited) { "EXITED code=$($p.ExitCode)"; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $null
for ($i = 0; $i -lt 20; $i++) { $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond); if ($win) { break }; Start-Sleep -Milliseconds 500 }
if (-not $win) { 'NO WINDOW'; exit 1 }

function ById([string]$id) {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function ComboText([string]$id) {
  $el = ById $id
  if (-not $el) { return '<absent>' }
  $sp = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern, [ref]$sp)) {
    $cur = $sp.Current.GetSelection()
    if ($cur -and $cur.Count -gt 0) { return $cur[0].Current.Name }
  }
  return $el.Current.Name
}
function ComboRect([string]$id) { $el = ById $id; if (-not $el) { return '<absent>' }; $r = $el.Current.BoundingRectangle; return "$([int]$r.X),$([int]$r.Y),$([int]$r.Width)x$([int]$r.Height)" }
function ComboSelectIndex([string]$id, [int]$index) {
  $el = ById $id
  if (-not $el) { return 'absent' }
  $ec = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$ec)) { $ec.Expand(); Start-Sleep -Milliseconds 800 }
  $items = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
  $target = @()
  foreach ($it in $items) { if ($it.Current.Name -and $it.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { $target += $it } }
  if (-not $target) { return 'no-items' }
  $pick = $target[$index]
  $si = $null
  if ($pick.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$si)) { $si.Select() } else { $pick.SetFocus() }
  if ($ec) { $ec.Collapse() }
  Start-Sleep -Milliseconds 400
  return "picked=$($pick.Current.Name)"
}
function ToggleState([string]$id) {
  $el = ById $id
  if (-not $el) { return '<absent>' }
  $tp = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) { return $tp.Current.ToggleState }
  return '<no-toggle>'
}
function ToggleClick([string]$id) {
  $el = ById $id
  $tp = $null
  if ($el -and $el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) { $tp.Toggle(); return 'ok' }
  return 'absent'
}
function Snap { "{0}|{1}|{2}|{3}|{4}|{5}|{6}" -f (ComboText 'cmbUiLanguage'),(ComboText 'cmbPlaySetPageDisplayMode'),(ComboText 'cmbBackgroundDisplayMode'),(ComboText 'cmbModThumbnailSize'),(ToggleState 'tglAlwaysOnTop'),(ToggleState 'tglOnlineThemeRefresh'),(ComboText 'cmbAlternativeExecutable') }
function Shot([string]$name) {
  $hwnd = $p.MainWindowHandle
  Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W4 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@ -ErrorAction SilentlyContinue
  [W4]::SetForegroundWindow($hwnd) | Out-Null
  Start-Sleep -Milliseconds 400
  $r = New-Object W4+RECT
  [W4]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
  $bmp.Save((Join-Path $shotDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

$nav = ById 'navSettings'
$s = $null
if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$s)) { $s.Select() }
Start-Sleep -Seconds 3
$sec = ById 'navSettingsAppearance'
$si = $null
if ($sec.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$si)) { $si.Select() }
Start-Sleep -Milliseconds 1200

$before = Snap
$rectBefore = ComboRect 'cmbBackgroundDisplayMode'
"BEFORE  = $before"
"RECT_BEFORE = $rectBefore"
Shot 'regress-before'

$r1 = ComboSelectIndex 'cmbBackgroundDisplayMode' 1
Start-Sleep -Milliseconds 1500
"CHANGE_BG = $r1"
$afterChange = Snap
$rectAfter = ComboRect 'cmbBackgroundDisplayMode'
"AFTER_CHANGE = $afterChange"
"RECT_AFTER = $rectAfter"
Shot 'regress-after-bg'

$t1 = ToggleClick 'tglAlwaysOnTop'
Start-Sleep -Milliseconds 1200
"TOGGLE_AOT = $t1"
$afterToggle = Snap
$rectToggle = ComboRect 'cmbBackgroundDisplayMode'
"AFTER_TOGGLE = $afterToggle"
"RECT_AFTER_TOGGLE = $rectToggle"
Shot 'regress-after-toggle'

# 还原：选项回到 0，置顶恢复原状态
ComboSelectIndex 'cmbBackgroundDisplayMode' 0 | Out-Null
Start-Sleep -Milliseconds 800
if ((ToggleState 'tglAlwaysOnTop') -ne $before.Split('|')[4]) { ToggleClick 'tglAlwaysOnTop' | Out-Null }
Start-Sleep -Milliseconds 800
"RESTORED = $(Snap)"

$failed = @()
if ($afterChange.Split('|')[0] -ne $before.Split('|')[0]) { $failed += 'uiLanguage-reset' }
if ($afterChange.Split('|')[1] -ne $before.Split('|')[1]) { $failed += 'playSetMode-reset' }
if ($afterChange.Split('|')[3] -ne $before.Split('|')[3]) { $failed += 'thumbnailSize-reset' }
if ($afterChange.Split('|')[5] -ne $before.Split('|')[5]) { $failed += 'themeRefresh-reset' }
if ($afterChange.Split('|')[2] -eq $before.Split('|')[2]) { $failed += 'bgMode-not-changed' }
if ($rectAfter -ne $rectBefore) { $failed += 'combo-rect-changed' }
if ($failed.Count -gt 0) { "FAILED=[$($failed -join ',')]"; 'DONE_WITH_FAILURES' } else { 'DONE' }
