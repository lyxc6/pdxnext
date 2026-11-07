# 冒烟：工坊加载期间能不能切页。
# 流程：启动 bin 产物 → 选「创意工坊」→ 1 秒内选「模组」→ 断言模组页的控件出来了。
# 用法：pwsh -File scripts/verify-workshop-nav.ps1
# 退出码：0 = 切页成功；1 = 断言失败或进程异常退出。
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$rootDir = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $rootDir 'src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe'
if (-not (Test-Path $exe)) { "NO_EXE $exe"; exit 1 }

Get-Process PdxLauncher.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.WorkingDirectory = Split-Path $exe
$p = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Seconds 8
$p.Refresh()
if ($p.HasExited) { "EXITED code=$($p.ExitCode)"; exit 1 }
"PID=$($p.Id)"

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $null
for ($i = 0; $i -lt 30; $i++) {
  $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
  if ($win) { break }
  Start-Sleep -Milliseconds 500
}
if (-not $win) {
  'NO_WINDOW'
  Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force
  exit 1
}

function ById([string]$id) {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function SelectNav([string]$id) {
  $el = $null
  for ($i = 0; $i -lt 40; $i++) {
    $el = ById $id
    if ($el) { break }
    Start-Sleep -Milliseconds 250
  }
  if (-not $el) { return $false }
  $pat = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) { $pat.Select(); return $true }
  $inv = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); return $true }
  return $false
}

if (-not (SelectNav 'navWorkshop')) { 'select navWorkshop: FAILED'; Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force; exit 1 }
'select navWorkshop: True'

# 工坊加载要 6 秒左右，这里故意只等 1 秒就切页：加载期间导航也必须立刻生效。
Start-Sleep -Milliseconds 1000
$sw = [System.Diagnostics.Stopwatch]::StartNew()
if (-not (SelectNav 'navPlaySets')) { 'select navPlaySets: FAILED'; Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force; exit 1 }
'select navPlaySets: True'

# 模组页的控件出现即视为切页成功（最多等 5 秒，覆盖页面构造时间）。
$marker = $null
while ($sw.ElapsedMilliseconds -lt 5000) {
  foreach ($id in @('lstMods', 'cmbPlaySets', 'txtModSearch')) {
    $marker = ById $id
    if ($marker) { break }
  }
  if ($marker) { break }
  Start-Sleep -Milliseconds 100
}

$errorLines = @()
if (-not $marker) {
  $errorLines += 'ASSERT FAILED: 模组页 5 秒内没有出现（切页被加载卡住）'
} else {
  "mods page marker: $($marker.Current.AutomationId) at $($sw.ElapsedMilliseconds) ms"
}

# 顺手确认工坊页已经让位（它的搜索框不该还在树里）。
if (ById 'txtWorkshopSearch') {
  $errorLines += 'ASSERT FAILED: 工坊页控件仍在（页面没切走）'
}

Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($errorLines.Count -gt 0) {
  $errorLines | ForEach-Object { $_ }
  "RESULT=FAIL"
  exit 1
}

"RESULT=PASS"
exit 0
