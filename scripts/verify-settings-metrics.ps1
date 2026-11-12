# 设置页布局度量自检：内容列 1040 DIP 居中、关键卡片撑满列宽、卡高不漂。
# 用法：pwsh -File scripts/verify-settings-metrics.ps1
# 依赖：Windows UI Automation（PowerShell 7），不依赖 Playwright。
# 退出码：0 = RESULT=PASS；1 = RESULT=FAIL（逐项打印实测值）。
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$rootDir = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $rootDir 'src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe'
$shotDir = Join-Path $rootDir '.scratch\rework'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class SettingsMetricsWin32
{
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
'@

$script:failures = @()
function Fail([string]$message)
{
    $script:failures += $message
    Write-Output "FAIL $message"
}

function ById($root, [string]$id)
{
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function WaitById($root, [string]$id, [int]$tries = 40, [int]$sleepMs = 250)
{
    for ($i = 0; $i -lt $tries; $i++)
    {
        $element = ById $root $id
        if ($element) { return $element }
        Start-Sleep -Milliseconds $sleepMs
    }
    return $null
}

function SelectSection($root, [string]$id)
{
    $element = WaitById $root $id
    if (-not $element) { return $false }

    $selection = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection))
    {
        $selection.Select()
        return $true
    }

    $invoke = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invoke))
    {
        $invoke.Invoke()
        return $true
    }

    return $false
}

function ScreenRect($element)
{
    $r = $element.Current.BoundingRectangle
    return [pscustomobject]@{
        Left = [double]$r.Left
        Top = [double]$r.Top
        Width = [double]$r.Width
        Height = [double]$r.Height
    }
}

function ToDip($rect, [double]$scale)
{
    return [pscustomobject]@{
        Left = $rect.Left / $scale
        Top = $rect.Top / $scale
        Width = $rect.Width / $scale
        Height = $rect.Height / $scale
    }
}

# 承载卡片的滚动区：ControlType=Pane 且 ClassName=ScrollViewer；取包含卡片中心、
# 面积最小的那个，避免误选 NavigationView 内部的滚动宿主。
function FindContainingScrollViewer($root, $cardRect)
{
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Pane)
    $panes = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $best = $null
    $bestArea = [double]::MaxValue
    $centerX = $cardRect.Left + ($cardRect.Width / 2)
    $centerY = $cardRect.Top + ($cardRect.Height / 2)

    foreach ($pane in $panes)
    {
        if ($pane.Current.ClassName -ne 'ScrollViewer') { continue }
        $r = $pane.Current.BoundingRectangle
        if ($r.Width -le 0 -or $r.Height -le 0) { continue }
        if ($centerX -lt $r.Left -or $centerX -gt $r.Right) { continue }
        if ($centerY -lt $r.Top -or $centerY -gt $r.Bottom) { continue }
        $area = $r.Width * $r.Height
        if ($area -lt $bestArea)
        {
            $best = $pane
            $bestArea = $area
        }
    }

    return $best
}

function Shot([string]$name)
{
    [SettingsMetricsWin32]::SetForegroundWindow($script:hwnd) | Out-Null
    Start-Sleep -Milliseconds 500
    $rect = New-Object SettingsMetricsWin32+RECT
    [SettingsMetricsWin32]::GetWindowRect($script:hwnd, [ref]$rect) | Out-Null

    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $left = [Math]::Max(0, $rect.Left)
    $top = [Math]::Max(0, $rect.Top)
    $right = [Math]::Min($screen.Width, $rect.Right)
    $bottom = [Math]::Min($screen.Height, $rect.Bottom)
    if ($right -le $left -or $bottom -le $top) { return }

    $width = $right - $left
    $height = $bottom - $top
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($left, $top, 0, 0, (New-Object System.Drawing.Size $width, $height))
    $path = Join-Path $shotDir "$name.png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Output "SHOT $path"
}

# 切节后滚动位置可能停在上一节；量卡片前先把所有可纵向滚动的 ScrollViewer 归零。
function ScrollPageToTop($root)
{
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ScrollViewer')
    $viewers = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    foreach ($viewer in $viewers)
    {
        $scroll = $null
        if ($viewer.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$scroll))
        {
            if ($scroll.Current.VerticallyScrollable)
            {
                $scroll.SetScrollPercent(-1, 0)
            }
        }
    }
    Start-Sleep -Milliseconds 500
}

# 布局是异步的：元素刚进 UIA 树时高度可能还是 1px。等到尺寸达到下限再量。
function WaitStableRect($element, [double]$scale, [int]$tries = 20)
{
    $minWidth = 900 * $scale
    $minHeight = 40 * $scale
    $rect = ScreenRect $element
    for ($i = 0; $i -lt $tries; $i++)
    {
        if ($rect.Width -ge $minWidth -and $rect.Height -ge $minHeight) { return $rect }
        Start-Sleep -Milliseconds 200
        $rect = ScreenRect $element
    }
    return $rect
}

$p = $null
try
{
    if (-not (Test-Path $exe)) { throw "NO_EXE $exe" }

    Get-Process PdxLauncher.App -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700

    # 必须 UseShellExecute=$false：未签名 worktree exe 走 ShellExecute 会弹安全警告（Win32 1223）。
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $exe
    $p = [System.Diagnostics.Process]::Start($psi)
    Start-Sleep -Seconds 10
    $p.Refresh()
    if ($p.HasExited) { throw "EXITED code=$($p.ExitCode)" }
    Write-Output "PID=$($p.Id)"

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    $window = $null
    for ($i = 0; $i -lt 40; $i++)
    {
        $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $processCondition)
        if ($window) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $window) { throw 'NO_WINDOW' }

    $hwnd = $p.MainWindowHandle
    for ($i = 0; $i -lt 20 -and (-not $hwnd -or $hwnd -eq [IntPtr]::Zero); $i++)
    {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        $hwnd = $p.MainWindowHandle
    }
    if (-not $hwnd -or $hwnd -eq [IntPtr]::Zero) { throw 'NO_HWND' }
    $script:hwnd = $hwnd

    $dpi = [SettingsMetricsWin32]::GetDpiForWindow($hwnd)
    if ($dpi -eq 0) { $dpi = 96 }
    $scale = [double]$dpi / 96.0
    $targetWidth = [int][Math]::Round(1700 * $scale)
    $targetHeight = [int][Math]::Round(900 * $scale)
    [SettingsMetricsWin32]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, $targetWidth, $targetHeight, 0x14) | Out-Null
    Start-Sleep -Milliseconds 1200
    $p.Refresh()
    Write-Output "DPI=$dpi SCALE=$scale WINDOW=${targetWidth}x${targetHeight}px"

    # 进入设置页，再切到「外观」。
    if (-not (SelectSection $window 'navSettings')) { throw 'NO_NAV_SETTINGS' }
    Start-Sleep -Seconds 3
    if ($p.HasExited) { throw "CRASHED code=$($p.ExitCode)" }
    if (-not (SelectSection $window 'navSettingsAppearance')) { throw 'NO_NAV_APPEARANCE' }
    Start-Sleep -Milliseconds 1500
    ScrollPageToTop $window

    $card = WaitById $window 'cardThemeSource' 40 250
    if (-not $card) { throw 'NO_CARD_THEME_SOURCE' }
    $cardPx = WaitStableRect $card $scale
    $cardDip = ToDip $cardPx $scale
    Write-Output ("MEASURE cardThemeSource: width={0:N1}DIP height={1:N1}DIP (raw {2:N0}x{3:N0}px)" -f $cardDip.Width, $cardDip.Height, $cardPx.Width, $cardPx.Height)

    $content = ById $window 'settingsContentColumn'
    if ($content)
    {
        $contentDip = ToDip (ScreenRect $content) $scale
        Write-Output ("MEASURE settingsContentColumn: left={0:N1}DIP width={1:N1}DIP" -f $contentDip.Left, $contentDip.Width)
    }

    if ([Math]::Abs($cardDip.Width - 1040) -gt 4)
    {
        Fail ("cardThemeSource 宽度 {0:N1} DIP，期望 1040±4" -f $cardDip.Width)
    }
    if ([Math]::Abs($cardDip.Height - 56) -gt 4)
    {
        Fail ("cardThemeSource 高度 {0:N1} DIP，期望 56±4" -f $cardDip.Height)
    }

    $scroll = FindContainingScrollViewer $window $cardPx
    if (-not $scroll)
    {
        Fail '找不到承载 cardThemeSource 的 ScrollViewer（ControlType=Pane、ClassName=ScrollViewer）'
    }
    else
    {
        $scrollPx = ScreenRect $scroll
        $scrollDip = ToDip $scrollPx $scale
        $expectedLeft = $scrollDip.Left + (($scrollDip.Width - $cardDip.Width) / 2)
        $deviation = [Math]::Abs($cardDip.Left - $expectedLeft)
        Write-Output ("MEASURE scrollViewer: left={0:N1}DIP width={1:N1}DIP" -f $scrollDip.Left, $scrollDip.Width)
        Write-Output ("MEASURE center: cardLeft={0:N1}DIP expectedLeft={1:N1}DIP deviation={2:N1}DIP (limit 8)" -f $cardDip.Left, $expectedLeft, $deviation)
        if ($deviation -gt 8)
        {
            Fail ("cardThemeSource 相对滚动区居中偏差 {0:N1} DIP，期望 ≤8" -f $deviation)
        }
    }

    Shot 'settings-metrics-appearance'

    # 切到「关于」，量折叠头的 SettingsExpander。
    if (-not (SelectSection $window 'navSettingsAbout')) { throw 'NO_NAV_ABOUT' }
    Start-Sleep -Milliseconds 1500
    $expander = WaitById $window 'expSettingsAbout' 40 250
    if (-not $expander) { throw 'NO_EXPANDER_ABOUT' }

    # 断言的是「默认折叠」的折叠头宽度；若此前被 UIA 展开过，先收回再量。
    $expandPattern = $null
    $expandTarget = $expander
    if (-not $expandTarget.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expandPattern))
    {
        $buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $expandTarget = $expander.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
        if ($expandTarget) { $expandTarget.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expandPattern) | Out-Null }
    }
    if ($expandPattern)
    {
        Write-Output "EXPANDER_STATE=$($expandPattern.Current.ExpandCollapseState)"
        if ($expandPattern.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded)
        {
            $expandPattern.Collapse()
            Start-Sleep -Milliseconds 800
        }
    }

    Start-Sleep -Milliseconds 500
    $expanderPx = WaitStableRect $expander $scale
    $expanderDip = ToDip $expanderPx $scale
    Write-Output ("MEASURE expSettingsAbout: width={0:N1}DIP height={1:N1}DIP (raw {2:N0}x{3:N0}px)" -f $expanderDip.Width, $expanderDip.Height, $expanderPx.Width, $expanderPx.Height)

    if ([Math]::Abs($expanderDip.Width - 1040) -gt 4)
    {
        Fail ("expSettingsAbout 宽度 {0:N1} DIP，期望 1040±4" -f $expanderDip.Width)
    }

    Shot 'settings-metrics-about'
}
catch
{
    Fail $_.Exception.Message
}
finally
{
    if ($p -and -not $p.HasExited)
    {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
}

if ($script:failures.Count -eq 0)
{
    Write-Output 'RESULT=PASS'
    exit 0
}

Write-Output 'RESULT=FAIL'
foreach ($failure in $script:failures) { Write-Output "  - $failure" }
exit 1
