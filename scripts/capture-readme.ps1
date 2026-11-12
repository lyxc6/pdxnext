# README 截图重截：当前游戏 / 游戏库 / 设置（外观）。
# 用法：powershell -ExecutionPolicy Bypass -File scripts/capture-readme.ps1
# 依赖：Windows UI Automation + System.Drawing，不需要 Playwright。
# 输出：docs\screenshot-home.png / screenshot-library.png / screenshot-settings.png
# 说明：沿用 HANDOFF 记录的方法——PrintWindow 在 WinUI 3 上会出陈旧帧，改用
#       SetWindowPos(topmost) + DwmGetWindowAttribute + CopyFromScreen；截完直接结束进程，
#       避免关闭窗口把截图专用尺寸写回 data\pdxnext.sqlite。
# 退出码：0 = RESULT=PASS；1 = RESULT=FAIL。
param(
    [int]$WidthDip = 1600,
    [int]$HeightDip = 900,
    [int]$OutputWidth = 1600,
    [string]$Exe
)

$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$rootDir = Split-Path -Parent $PSScriptRoot
if (-not $Exe) {
    $Exe = Join-Path $rootDir 'src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe'
}
$docsDir = Join-Path $rootDir 'docs'

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class ReadmeCaptureWin32
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);

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

[ReadmeCaptureWin32]::SetProcessDPIAware() | Out-Null

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

function SelectNav($root, [string]$id)
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
            if ($scroll.Current.VerticallyScrollable) { $scroll.SetScrollPercent(-1, 0) }
        }
    }
    Start-Sleep -Milliseconds 600
}

function GetFrameRect([IntPtr]$hwnd)
{
    $rect = New-Object ReadmeCaptureWin32+RECT
    $hr = [ReadmeCaptureWin32]::DwmGetWindowAttribute($hwnd, 9, [ref]$rect, 16)
    if ($hr -ne 0 -or $rect.Right -le $rect.Left -or $rect.Bottom -le $rect.Top)
    {
        # DWM 取不到时退回 GetWindowRect 语义：用 SetWindowPos 的目标值不可靠，直接报错。
        throw "DWM_FRAME_BOUNDS_FAILED hr=$hr"
    }
    return $rect
}

function SetTopmost([IntPtr]$hwnd, [bool]$on)
{
    $after = if ($on) { [IntPtr](-1) } else { [IntPtr](-2) }
    [ReadmeCaptureWin32]::SetWindowPos($hwnd, $after, 0, 0, 0, 0, 0x0002 -bor 0x0001 -bor 0x0040) | Out-Null
}

function MovePointerAway([IntPtr]$hwnd)
{
    $rect = GetFrameRect $hwnd
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    # 选一个窗口矩形之外的点，避免浮层滚动条因指针悬停而显形。
    $x = if ($rect.Left -gt 20) { 4 } else { $screen.Width - 4 }
    $y = if ($rect.Bottom -lt ($screen.Height - 20)) { $rect.Bottom + 8 } else { 4 }
    [ReadmeCaptureWin32]::SetCursorPos($x, $y) | Out-Null
}

function SaveScreenshot([IntPtr]$hwnd, [string]$name)
{
    SetTopmost $hwnd $true
    [ReadmeCaptureWin32]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 900

    $rect = GetFrameRect $hwnd
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { throw "BAD_FRAME ${width}x${height}" }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
    $graphics.Dispose()

    $path = Join-Path $docsDir "$name.png"
    $final = $bitmap
    if ($OutputWidth -gt 0 -and $width -gt $OutputWidth)
    {
        $outHeight = [int][Math]::Round($height * $OutputWidth / $width)
        $final = New-Object System.Drawing.Bitmap $OutputWidth, $outHeight
        $resizeGraphics = [System.Drawing.Graphics]::FromImage($final)
        $resizeGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $resizeGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $resizeGraphics.DrawImage($bitmap, 0, 0, $OutputWidth, $outHeight)
        $resizeGraphics.Dispose()
    }

    $final.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $finalWidth = $final.Width
    $finalHeight = $final.Height
    if ($final -ne $bitmap) { $final.Dispose() }
    $bitmap.Dispose()

    SetTopmost $hwnd $false
    Write-Host ("SHOT {0} {1}x{2}" -f $path, $finalWidth, $finalHeight)
    return [pscustomobject]@{ Path = $path; Width = $finalWidth; Height = $finalHeight }
}

function SizeWindow([IntPtr]$hwnd, [int]$desiredWidth, [int]$desiredHeight)
{
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $windowWidth = [Math]::Min($desiredWidth, $screen.Width)
    $windowHeight = [Math]::Min($desiredHeight, $screen.Height)
    $left = [Math]::Max(0, [int](($screen.Width - $windowWidth) / 2))
    $top = [Math]::Max(0, [int](($screen.Height - $windowHeight) / 2))

    # DWM 的扩展边框比 SetWindowPos 的目标小几像素；按实测差值迭代收敛。
    for ($i = 0; $i -lt 4; $i++)
    {
        [ReadmeCaptureWin32]::SetWindowPos($hwnd, [IntPtr]::Zero, $left, $top, $windowWidth, $windowHeight, 0x0040) | Out-Null
        Start-Sleep -Milliseconds 900
        $rect = GetFrameRect $hwnd
        $currentWidth = $rect.Right - $rect.Left
        $currentHeight = $rect.Bottom - $rect.Top
        Write-Output ("FRAME {0} {1}px target={2}px" -f $i, ("{0}x{1}" -f $currentWidth, $currentHeight), ("{0}x{1}" -f $desiredWidth, $desiredHeight))
        $deltaWidth = $desiredWidth - $currentWidth
        $deltaHeight = $desiredHeight - $currentHeight
        if ([Math]::Abs($deltaWidth) -le 2 -and [Math]::Abs($deltaHeight) -le 2) { return }
        $windowWidth = [Math]::Min($screen.Width, $windowWidth + $deltaWidth)
        $windowHeight = [Math]::Min($screen.Height, $windowHeight + $deltaHeight)
    }
}

$p = $null
try
{
    if (-not (Test-Path -LiteralPath $Exe)) { throw "NO_EXE $Exe" }

    Get-Process PdxLauncher.App -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800

    # 未签名 worktree exe 走 ShellExecute 会弹安全警告，必须用 CreateProcess。
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $Exe
    $p = [System.Diagnostics.Process]::Start($psi)
    Start-Sleep -Seconds 10
    $p.Refresh()
    if ($p.HasExited) { throw "EXITED code=$($p.ExitCode)" }
    Write-Output "PID=$($p.Id)"

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    $window = $null
    for ($i = 0; $i -lt 60; $i++)
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

    $dpi = [ReadmeCaptureWin32]::GetDpiForWindow($hwnd)
    if ($dpi -eq 0) { $dpi = 96 }
    $scale = [double]$dpi / 96.0
    $targetWidth = [int][Math]::Round($WidthDip * $scale)
    $targetHeight = [int][Math]::Round($HeightDip * $scale)
    Write-Output ("DPI={0} SCALE={1:N2} TARGET={2}x{3}px OUTPUT_WIDTH={4}" -f $dpi, $scale, $targetWidth, $targetHeight, $OutputWidth)
    SizeWindow $hwnd $targetWidth $targetHeight

    $theme = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue).AppsUseLightTheme
    Write-Output ("THEME={0}" -f $(if ($theme -eq 0) { 'dark' } else { 'light' }))
    if ($theme -ne 0) { Fail '系统为浅色主题，README 截图按深色主题约定' }

    # 1) 当前游戏：启动后默认就在主页。有官方字标时 txtHomeGameTitle 是折叠的，
    #    所以等到「开始游戏」按钮出现即视为主页就绪。
    $startButton = WaitById $window 'btnStartGame' 40 250
    if (-not $startButton) { Fail 'NO_HOME_START_BUTTON' }
    ScrollPageToTop $window
    MovePointerAway $hwnd
    Start-Sleep -Milliseconds 1200
    $shot = SaveScreenshot $hwnd 'screenshot-home'
    if ($shot.Width -ne $OutputWidth) { Fail ("home 宽度 {0}，期望 {1}" -f $shot.Width, $OutputWidth) }

    # 2) 游戏库。
    if (-not (SelectNav $window 'navLibrary')) { throw 'NO_NAV_LIBRARY' }
    Start-Sleep -Seconds 2
    $count = WaitById $window 'txtLibraryCount' 40 250
    if (-not $count) { Fail 'NO_LIBRARY_COUNT' }
    $busy = ById $window 'ringLibraryBusy'
    if ($busy) { Start-Sleep -Seconds 2 }
    ScrollPageToTop $window
    MovePointerAway $hwnd
    Start-Sleep -Milliseconds 1200
    $shot = SaveScreenshot $hwnd 'screenshot-library'
    if ($shot.Width -ne $OutputWidth) { Fail ("library 宽度 {0}，期望 {1}" -f $shot.Width, $OutputWidth) }

    # 3) 设置 → 外观（首页第一节）。
    if (-not (SelectNav $window 'navSettings')) { throw 'NO_NAV_SETTINGS' }
    Start-Sleep -Seconds 2
    if (-not (SelectNav $window 'navSettingsAppearance')) { throw 'NO_NAV_APPEARANCE' }
    if (-not (WaitById $window 'txtThemeSource' 60 250)) { Fail 'NO_SETTINGS_THEME_SOURCE' }
    ScrollPageToTop $window
    MovePointerAway $hwnd
    Start-Sleep -Milliseconds 1200
    $shot = SaveScreenshot $hwnd 'screenshot-settings'
    if ($shot.Width -ne $OutputWidth) { Fail ("settings 宽度 {0}，期望 {1}" -f $shot.Width, $OutputWidth) }

    foreach ($name in @('screenshot-home', 'screenshot-library', 'screenshot-settings'))
    {
        $path = Join-Path $docsDir "$name.png"
        if (-not (Test-Path -LiteralPath $path)) { Fail "MISSING $path"; continue }
        $image = [System.Drawing.Image]::FromFile($path)
        Write-Output ("CHECK {0} {1}x{2}" -f $path, $image.Width, $image.Height)
        $image.Dispose()
    }
}
catch
{
    Fail $_.Exception.Message
}
finally
{
    if ($p -and -not $p.HasExited)
    {
        # 直接结束进程，不触发关闭保存，避免把截图尺寸写进设置。
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
