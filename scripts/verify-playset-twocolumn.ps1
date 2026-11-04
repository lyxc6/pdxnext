# Playset page two-column layout smoke test.
# Usage: pwsh -File scripts/verify-playset-twocolumn.ps1
# What it does (layout only, no mod data is added or removed):
#   1. open Settings > Appearance, remember the current playset layout option, pick
#      "two column" (option index 1: official, twoColumn, placeholder)
#   2. open the playset page and assert the two-column surface is exposed through UIA
#      (left candidate list, right playset list, both search boxes, shared toolbar)
#      and that the original-layout-only controls are gone
#   3. screenshot to .scratch\rework\playset-twocolumn.png
#   4. switch back to the original launcher layout and assert the original surface
#      is back and the two-column surface is gone
#   5. restore the option the user had before the run
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
# UseShellExecute=$false: an unsigned exe started through ShellExecute raises the
# "Open File - Security Warning" dialog (cancel = Win32 1223).
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

function Ids {
  $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $out = @()
  foreach ($e in $all) { $id = $e.Current.AutomationId; if ($id) { $out += $id } }
  return ($out | Sort-Object -Unique)
}

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

# Wait until any of the given ids shows up (pages load asynchronously, so fixed
# sleeps are not enough on a loaded machine).
function WaitAnyOf([string[]]$ids, [int]$tries = 40) {
  for ($i = 0; $i -lt $tries; $i++) {
    foreach ($id in $ids) {
      if (ById $id) { return $id }
    }
    Start-Sleep -Milliseconds 500
  }
  return $null
}

# Settings -> Appearance: the settings page rebuilds its section list on load, so
# the appearance entry can appear late. Re-click the settings entry and retry.
function OpenAppearance([int]$tries = 3) {
  for ($i = 0; $i -lt $tries; $i++) {
    Navigate 'navSettings' | Out-Null
    if (WaitAnyOf -ids @('navSettingsAppearance') -tries 30) {
      Navigate 'navSettingsAppearance' | Out-Null
      Start-Sleep -Milliseconds 800
      return $true
    }
    Start-Sleep -Seconds 1
  }
  return $false
}

# Click a NavigationView item (works for both SelectionItem and Invoke providers).
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

# Selected index of a drop down (PdxDropDown exposes Selection + ExpandCollapse).
function DropDownIndex([string]$id) {
  $el = ById $id
  if (-not $el) { return -1 }
  $ec = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$ec)) {
    $ec.Expand()
    Start-Sleep -Milliseconds 800
  }
  $items = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
  $names = @()
  foreach ($it in $items) { if ($it.Current.Name) { $names += $it.Current.Name } }
  $index = -1
  $sp = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern, [ref]$sp)) {
    $cur = $sp.Current.GetSelection()
    if ($cur -and $cur.Count -gt 0) {
      for ($i = 0; $i -lt $names.Count; $i++) {
        if ($names[$i] -eq $cur[0].Current.Name) { $index = $i; break }
      }
    }
  }
  if ($ec) { $ec.Collapse() }
  Start-Sleep -Milliseconds 300
  return $index
}

function SelectDropDownIndex([string]$id, [int]$index) {
  $el = ById $id
  if (-not $el) { return $false }
  $ec = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$ec)) {
    $ec.Expand()
    Start-Sleep -Milliseconds 800
  }
  $items = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
  $target = @()
  foreach ($it in $items) { if ($it.Current.Name) { $target += $it } }
  $ok = $false
  if ($target.Count -gt $index) {
    $pick = $target[$index]
    $si = $null
    if ($pick.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$si)) { $si.Select(); $ok = $true }
    else { $pick.SetFocus(); $ok = $true }
  }
  if ($ec) { $ec.Collapse() }
  Start-Sleep -Milliseconds 900
  return $ok
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
public class W3Tc {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

function Shot([string]$name) {
  # The window rect can be 0x0 when another window owns the foreground, so retry a
  # few times instead of silently skipping the screenshot.
  $w = 0; $hh = 0; $r = New-Object W3Tc+RECT
  for ($i = 0; $i -lt 5; $i++) {
    [W3Tc]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 700
    [W3Tc]::GetWindowRect($hwnd, [ref]$r) | Out-Null
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

function Check-Missing([string]$label, [string[]]$want) {
  $ids = Ids
  $missing = @()
  foreach ($w in $want) { if ($ids -notcontains $w) { $missing += $w } }
  if ($missing.Count -gt 0) { $script:failed++; "$($label): missing=[$($missing -join ',')]" }
  else { "$($label): ok" }
}

function Check-Absent([string]$label, [string[]]$unwanted) {
  $ids = Ids
  $present = @()
  foreach ($u in $unwanted) { if ($ids -contains $u) { $present += $u } }
  if ($present.Count -gt 0) { $script:failed++; "$($label): unexpected=[$($present -join ',')]" }
  else { "$($label): ok" }
}

# Either a populated list or its empty-state text is exposed: both are valid, data dependent.
function Check-AnyOf([string]$label, [string[]]$any) {
  $ids = Ids
  foreach ($a in $any) { if ($ids -contains $a) { "$($label): ok ($a)"; return } }
  $script:failed++
  "$($label): none of [$($any -join ',')]"
}

$failed = 0
$modeId = 'cmbPlaySetPageDisplayMode'

if (-not (OpenAppearance)) { 'NO_NAV_APPEARANCE'; Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; exit 1 }

$original = DropDownIndex $modeId
"ORIGINAL_INDEX=$original"
if ($original -lt 0) { $failed++ }

SelectDropDownIndex $modeId 1 | Out-Null
$now = DropDownIndex $modeId
"SWITCHED_INDEX=$now"
if ($now -ne 1) { $failed++ }

# Back to the playset page: the layout is picked up when the page (re)loads.
if (-not (Navigate 'navPlaySets')) { 'NO_NAV_PLAYSETS'; $failed++ }
WaitAnyOf -ids @('cmbPlaySets', 'lstModCandidates', 'lstMods', 'txtPlaySetPlaceholder') -tries 30 | Out-Null
Start-Sleep -Milliseconds 1200
if ($p.HasExited) { "CRASHED code=$($p.ExitCode)"; exit 1 }

Check-Missing 'two-column chrome' @('cmbPlaySets', 'btnActivatePlaySet', 'txtModsStatus', 'txtCandidateSearch', 'txtModSearchTwoCol')
Check-Missing 'two-column search boxes bound' @('txtCandidateSearch', 'txtModSearchTwoCol')
Check-AnyOf 'left candidate surface' @('lstModCandidates', 'txtCandidateNoPlaySet', 'txtCandidateEmpty')
Check-AnyOf 'right playset surface' @('lstModsTwoColumn', 'txtTwoColumnEmpty', 'txtTwoColumnNoMatch')
Check-Absent 'original-only controls hidden' @('btnAddMods', 'lstMods', 'txtModListEmpty')
Shot 'playset-twocolumn'

# Switch back to the original launcher layout and confirm nothing regressed.
OpenAppearance | Out-Null
SelectDropDownIndex $modeId 0 | Out-Null
"RESTORED_INDEX=$(DropDownIndex $modeId)"
Navigate 'navPlaySets' | Out-Null
WaitAnyOf -ids @('cmbPlaySets', 'lstMods', 'lstModCandidates', 'txtPlaySetPlaceholder') -tries 30 | Out-Null
Start-Sleep -Milliseconds 1200
Check-Missing 'original layout chrome' @('cmbPlaySets', 'btnAddMods', 'txtModSearch', 'txtModsStatus')
Check-AnyOf 'original playset surface' @('lstMods', 'txtModListEmpty')
Check-Absent 'two-column surface hidden' @('lstModsTwoColumn', 'lstModCandidates')
Shot 'playset-official-after'

# Put back whatever the user had before the run (0 = official, 1 = two column, 2 = placeholder).
if ($original -ge 0 -and $original -ne 0) {
  OpenAppearance | Out-Null
  SelectDropDownIndex $modeId $original | Out-Null
  "USER_INDEX_RESTORED=$(DropDownIndex $modeId)"
}

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
"FAILED=$failed"
if ($failed -eq 0) { 'DONE' } else { 'DONE_WITH_FAILURES' }
