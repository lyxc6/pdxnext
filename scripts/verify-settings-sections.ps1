# 设置页分节自检：逐节切换左侧分类，检查该节应有的 AutomationId 是否都在。
# 用法：pwsh -File scripts/verify-settings-sections.ps1
# 依赖：Windows UI Automation（Windows PowerShell 5.1 / PowerShell 7 均可），未用 Playwright。
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
# 注意：必须用 UseShellExecute=$false 启动。worktree 里的未签名 exe 走 ShellExecute
# 会触发「打开文件 - 安全警告」（取消 = Win32 1223），Start-Process 会报「操作已被用户取消」。
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

# 进入设置页：首页可能还在加载，等导航项出现
$nav = $null
for ($i = 0; $i -lt 40; $i++) {
  $nav = ById 'navSettings'
  if ($nav) { break }
  Start-Sleep -Milliseconds 500
}
if (-not $nav) { 'NO_NAV_SETTINGS'; exit 1 }
$sel = $null
if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select() }
Start-Sleep -Seconds 3
if ($p.HasExited) { "CRASHED code=$($p.ExitCode)"; exit 1 }

# 每节应有的 AutomationId。
# 说明：DLC 简介、公告、Mod 缩略图这几块已从「联网」迁到「缓存」，联网只剩代理三卡。
$sections = @(
  @{ id='navSettingsAppearance'; name='appearance'; want='txtThemeSource,txtThemeLastCheck,txtThemeNextCheck,tglOnlineThemeRefresh,btnCheckThemeNow,cmbUiLanguage,cmbPlaySetPageDisplayMode,cmbBackgroundDisplayMode,cmbModThumbnailSize,tglAlwaysOnTop' },
  @{ id='navSettingsLaunch';     name='launch';     want='txtExePath,txtExeArgs,cmbAlternativeExecutable' },
  @{ id='navSettingsPaths';      name='paths';      want='txtGameDataPath,btnOpenDataDirectory,txtDlcPath,txtModDir,txtFormatVersion,txtLoadFilePath,txtAppDataDirectory,btnOpenAppDataDirectory,txtLegacyDataDirectory,btnClearLegacyData' },
  @{ id='navSettingsOnline';     name='online';     want='cmbProxyMode,txtProxyUrl,txtProxyStatus' },
  @{ id='navSettingsCache';      name='cache';      want='btnRefreshCache,btnClearAllCaches,tglOnlineDlcDescription,cmbDlcDescriptionRetention,tglOnlineNewsFeed,tglOnlineModThumbnail,cmbModThumbnailRetention,txtModThumbnailCachePath' },
  @{ id='navSettingsAdvanced';   name='advanced';   want='tglWriteDlcSignature,btnRegenerateDlcSignature' }
)

$hwnd = $p.MainWindowHandle
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W3 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

function Shot([string]$name) {
  [W3]::SetForegroundWindow($hwnd) | Out-Null
  Start-Sleep -Milliseconds 500
  $r = New-Object W3+RECT
  [W3]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
  $bmp.Save((Join-Path $shotDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

function ScrollToBottom {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ScrollViewer')
  $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)
  foreach ($e in $all) {
    $sp = $null
    if ($e.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$sp)) {
      if ($sp.Current.VerticallyScrollable) {
        $sp.SetScrollPercent(-1, 100)
        Start-Sleep -Milliseconds 700
        return $true
      }
    }
  }
  return $false
}

$failed = 0
foreach ($s in $sections) {
  $el = ById $s.id
  if (-not $el) { "MISSING_NAV=$($s.id)"; $failed++; continue }
  $si = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$si)) {
    $si.Select()
  } else {
    $inv = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke() }
  }
  Start-Sleep -Milliseconds 1200
  $ids = Ids
  $missing = @()
  foreach ($w in $s.want.Split(',')) { if ($ids -notcontains $w) { $missing += $w } }
  if ($missing.Count -gt 0) { $failed++ }
  "$($s.name): count=$($ids.Count) missing=[$($missing -join ',')]"

  Shot "settings-$($s.name)"

  if ($s.name -eq 'cache' -and (ScrollToBottom)) {
    Shot 'settings-cache-bottom'
  }
}

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
"FAILED_SECTIONS=$failed"
if ($failed -eq 0) { 'DONE' } else { 'DONE_WITH_MISSING' }
