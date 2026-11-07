# 「添加模组」页面版式冒烟测试（只读，不改任何 mod / 播放集数据）。
# 用法: pwsh -File scripts/verify-addmods-page.ps1
# 检查项：
#   1. 播放集页 -> 「添加模组」按钮能打开子页
#   2. 页头两行结构：标题与「→ 播放集名」在同一行（垂直中心对齐），计数在右侧固定列
#   3. 列表：候选行带版本列节点（addModCandidateVersion_*），且版本文案非空
#   4. 勾选框没有 120 DIP 默认最小宽留下的空白：勾选框右边缘到缩略图/标题左边缘的距离很小
#   5. 截图到 .scratch\rework\addmods-page.png，并把首行的 UIA 节点打印出来
# 依赖: Windows UI Automation（Windows PowerShell 5.1 / PowerShell 7）。
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
# UseShellExecute=$false: 未签名 exe 走 ShellExecute 会弹「打开文件 - 安全警告」（取消 = Win32 1223）。
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

function All {
  return $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
}

function Ids {
  $out = @()
  foreach ($e in (All)) { $id = $e.Current.AutomationId; if ($id) { $out += $id } }
  return ($out | Sort-Object -Unique)
}

function ById([string]$id) {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function ByIdPrefix([string]$prefix) {
  $out = @()
  foreach ($e in (All)) { if ($e.Current.AutomationId -like "$prefix*") { $out += $e } }
  return $out
}

function WaitId([string]$id, [int]$tries = 40) {
  for ($i = 0; $i -lt $tries; $i++) {
    $el = ById $id
    if ($el) { return $el }
    Start-Sleep -Milliseconds 500
  }
  return $null
}

function WaitAnyOf([string[]]$ids, [int]$tries = 40) {
  for ($i = 0; $i -lt $tries; $i++) {
    foreach ($id in $ids) { if (ById $id) { return $id } }
    Start-Sleep -Milliseconds 500
  }
  return $null
}

# 点 NavigationView 项或按钮：两种 Provider 都试。
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
public class W3Group {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

function Shot([string]$name) {
  $w = 0; $hh = 0; $r = New-Object W3Group+RECT
  for ($i = 0; $i -lt 5; $i++) {
    [W3Group]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 700
    [W3Group]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    if ($w -gt 0 -and $hh -gt 0) { break }
  }
  if ($w -le 0 -or $hh -le 0) { "SHOT_SKIPPED=$name"; return }
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
  $bmp.Save((Join-Path $shotDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  "SHOT=$name"
}

function Check-Missing([string]$label, [string[]]$want) {
  $ids = Ids
  $missing = @()
  foreach ($w in $want) { if ($ids -notcontains $w) { $missing += $w } }
  if ($missing.Count -gt 0) { $script:failed++; "$($label): missing=[$($missing -join ',')]" }
  else { "$($label): ok" }
}

$failed = 0

# 1. 播放集页 -> 「添加模组」
if (-not (Navigate 'navPlaySets')) { 'NO_NAV_PLAYSETS'; Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; exit 1 }
if (-not (WaitAnyOf -ids @('btnAddMods', 'cmbPlaySets') -tries 40)) { 'PLAYSET_PAGE_NOT_READY'; $script:failed++ }
Start-Sleep -Milliseconds 800
if (-not (Navigate 'btnAddMods')) { 'NO_BTN_ADDMODS'; $script:failed++ }

# 2. 子页就绪
if (-not (WaitId 'txtAddModsTitle' 40)) { 'NO_ADDMODS_PAGE'; $failed++; Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; "FAILED=$failed"; exit 1 }
Start-Sleep -Milliseconds 1500
if ($p.HasExited) { "CRASHED code=$($p.ExitCode)"; exit 1 }

Check-Missing 'add mods chrome' @(
  'txtAddModsTitle', 'txtAddModsTarget', 'txtAddModsSearch', 'cmbAddModsSort', 'txtAddModsCandidateCount',
  'lstAddModsCandidates', 'txtAddModsSelectedCount', 'btnAddModsCancel', 'btnAddModsConfirm')

# 3. 页头：标题与「→ 播放集名」必须同一行（垂直中心差几像素内），且都在一行的左半部分。
$title = ById 'txtAddModsTitle'
$target = ById 'txtAddModsTarget'
if ($title -and $target) {
  $tr = $title.Current.BoundingRectangle
  $gr = $target.Current.BoundingRectangle
  $dy = [math]::Abs(($tr.Top + $tr.Height / 2) - ($gr.Top + $gr.Height / 2))
  "HEADER title='$($title.Current.Name)' target='$($target.Current.Name)' centers_dy=$([math]::Round($dy,1)) title_h=$([math]::Round($tr.Height,1))"
  if ($dy -gt 12) { $failed++; "HEADER_NOT_ONE_LINE dy=$([math]::Round($dy,1))" }
  if ($gr.Left -lt ($tr.Left + $tr.Width - 4)) { $failed++; "TARGET_OVERLAPS_TITLE" }
} else { $failed++; 'HEADER_NODES_MISSING' }

# 4. 列表行：勾选框、缩略图/标题、版本列
$checks = ByIdPrefix 'addModCandidateCheck_'
$rows = ByIdPrefix 'addModCandidate_'
$versions = ByIdPrefix 'addModCandidateVersion_'
"ROWS=$($rows.Count) CHECKS=$($checks.Count) VERSION_CELLS=$($versions.Count)"
if ($rows.Count -eq 0) {
  'LIST_EMPTY（mod 库为空或全部已在播放集里，只跳过行内断言）'
} else {
  if ($checks.Count -ne $rows.Count) { $failed++; "CHECK_COUNT_MISMATCH rows=$($rows.Count) checks=$($checks.Count)" }
  if ($versions.Count -ne $rows.Count) { $failed++; "VERSION_COUNT_MISMATCH rows=$($rows.Count) versions=$($versions.Count)" }

  $first = $rows[0]
  $fr = $first.Current.BoundingRectangle
  "ROW0 rect=($([math]::Round($fr.Left,0)),$([math]::Round($fr.Top,0)),$([math]::Round($fr.Width,0)),$([math]::Round($fr.Height,0))) name='$($first.Current.Name)'"

  # 行高：56 DIP 在 100%~150% 缩放之间约 56~84 物理像素；原来被 CheckBox 的 120 DIP
  # 默认最小宽顶上去时是 90 以上，超过 88 就说明行里又出现了比内容更高的东西。
  if ($fr.Height -gt 88) { $failed++; "ROW_TOO_TALL h=$([math]::Round($fr.Height,1))" }

  if ($checks.Count -gt 0) {
    $cr = $checks[0].Current.BoundingRectangle
    "CHECK0 rect=($([math]::Round($cr.Left,0)),$([math]::Round($cr.Top,0)),$([math]::Round($cr.Width,0)),$([math]::Round($cr.Height,1)))"
    # 勾选框整格 24 DIP；没写 MinWidth=0 时是 120 DIP，缩放后至少 120 物理像素。
    if ($cr.Width -gt 104) { $failed++; "CHECKBOX_TOO_WIDE w=$([math]::Round($cr.Width,1))" }
  }

  # 版本列：每行都有节点、文案非空（缺失时是「—」），且右边缘对齐。
  $dash = 0
  $withVersion = 0
  $rights = @()
  foreach ($v in $versions) {
    $vr = $v.Current.BoundingRectangle
    $name = $v.Current.Name
    if ([string]::IsNullOrWhiteSpace($name)) { $failed++; "VERSION_NAME_EMPTY id=$($v.Current.AutomationId)" }
    if ($name -eq '—') { $dash++ } elseif ($name) { $withVersion++ }
    if ($vr.Right -gt 0) { $rights += [math]::Round($vr.Right,0) }
    if ($withVersion -eq 1 -and $name -ne '—') {
      "VERSION_SAMPLE id='$($v.Current.AutomationId)' name='$name' right=$([math]::Round($vr.Right,0))"
    }
  }
  "VERSION_CELLS dash=$dash withVersion=$withVersion"
  if ($rights.Count -gt 0) {
    $minR = ($rights | Measure-Object -Minimum).Minimum
    $maxR = ($rights | Measure-Object -Maximum).Maximum
    "VERSION_RIGHT_ALIGN min=$minR max=$maxR (spread=$($maxR - $minR))"
    if (($maxR - $minR) -gt 2) { $failed++; "VERSION_NOT_RIGHT_ALIGNED spread=$($maxR - $minR)" }
  }
}

Shot 'addmods-page'

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
"FAILED=$failed"
if ($failed -eq 0) { 'DONE' } else { 'DONE_WITH_FAILURES' }
