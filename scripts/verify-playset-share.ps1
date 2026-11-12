# Playset import/export toolbar smoke test.
# Usage: pwsh -File scripts/verify-playset-share.ps1
# What it does (UI only; no data is imported or exported):
#   1. open the playset page
#   2. assert the two new toolbar buttons (btnImportPlaySet / btnExportPlaySet)
#      are exposed through UIA, enabled according to the page state, and fully
#      inside the window (the toolbar wraps instead of clipping the right edge)
#   3. screenshot to .scratch\rework\playset-share-toolbar.png
# Dependencies: Windows UI Automation (Windows PowerShell 5.1 / PowerShell 7).
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
Start-Sleep -Milliseconds 700
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.WorkingDirectory = Split-Path $exe
$p = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Seconds 10
$p.Refresh()
if ($p.HasExited) { "EXITED code=$($p.ExitCode)"; exit 1 }
"PID=$($p.Id)"

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $null
for ($i = 0; $i -lt 20; $i++) {
  $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
  if ($win) { break }
  Start-Sleep -Milliseconds 500
}
if (-not $win) { 'NO WINDOW'; exit 1 }

function ById([string]$id) {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function WaitId([string]$id, [int]$tries = 40) {
  for ($i = 0; $i -lt $tries; $i++) {
    $el = ById $id
    if ($el) { return $el }
    Start-Sleep -Milliseconds 500
  }
  return $null
}

function Navigate([string]$id) {
  $el = WaitId $id
  if (-not $el) { return $false }
  $si = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$si)) {
    $si.Select()
  } else {
    $inv = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke() }
  }
  Start-Sleep -Milliseconds 1500
  return $true
}

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 20; $i++) {
  $p.Refresh()
  if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $p.MainWindowHandle; break }
  Start-Sleep -Milliseconds 500
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W3Share {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

function Shot([string]$name) {
  $w = 0; $hh = 0; $r = New-Object W3Share+RECT
  for ($i = 0; $i -lt 5; $i++) {
    [W3Share]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 700
    [W3Share]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    if ($w -gt 0 -and $hh -gt 0) { break }
  }
  if ($w -le 0 -or $hh -le 0) { "SHOT_SKIPPED=$name"; return }
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
  $bmp.Save((Join-Path $shotDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

function Check-InWindow([string]$label, $rect, $window) {
  $w = $window.Right - $window.Left
  $ok = $rect.Width -gt 0 -and $rect.Left -ge 0 -and ($rect.Left + $rect.Width) -le $w
  if ($ok) { "$($label): ok [$($rect.Left),$($rect.Top),$($rect.Width)x$($rect.Height)]" }
  else { $script:failed++; "$($label): clipped [$($rect.Left),$($rect.Top),$($rect.Width)x$($rect.Height)] window=$w" }
}

$failed = 0
if (-not (Navigate 'navPlaySets')) { 'NO_NAV_PLAYSETS'; Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; exit 1 }
WaitId 'cmbPlaySets' 30 | Out-Null
Start-Sleep -Milliseconds 1500
if ($p.HasExited) { "CRASHED code=$($p.ExitCode)"; exit 1 }

$wr = New-Object W3Share+RECT
[W3Share]::GetWindowRect($hwnd, [ref]$wr) | Out-Null
$window = New-Object psobject -Property @{ Left = 0; Right = ($wr.Right - $wr.Left); Top = 0; Bottom = ($wr.Bottom - $wr.Top) }

$import = WaitId 'btnImportPlaySet' 20
$export = WaitId 'btnExportPlaySet' 20
if (-not $import) { $failed++; 'MISSING btnImportPlaySet' } else {
  "IMPORT_NAME=$($import.Current.Name) ENABLED=$($import.Current.IsEnabled)"
  Check-InWindow 'import button' $import.Current.BoundingRectangle $window
}
if (-not $export) { $failed++; 'MISSING btnExportPlaySet' } else {
  "EXPORT_NAME=$($export.Current.Name) ENABLED=$($export.Current.IsEnabled)"
  Check-InWindow 'export button' $export.Current.BoundingRectangle $window
}

Shot 'playset-share-toolbar'

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
"FAILED=$failed"
if ($failed -eq 0) { 'DONE' } else { 'DONE_WITH_FAILURES' }
