<#
.SYNOPSIS
    Verify the locally installed PdxNext executable matches version.props.

.DESCRIPTION
    Release rule: after building the installer, install it locally and run this
    script. It checks every existing per-user / per-machine install location, so
    a stale installed copy cannot be mistaken for the new build.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/verify-installed-version.ps1
#>
param(
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $root 'version.props'
if (-not (Test-Path -LiteralPath $versionFile)) {
    Write-Error "version.props not found: $versionFile"
    exit 1
}

$version = ([xml](Get-Content -LiteralPath $versionFile -Raw)).Project.PropertyGroup.Version
$expected = "$version.0"

$candidates = @()
if ($Path) {
    $candidates += $Path
} else {
    $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\PdxNext\PdxLauncher.App.exe')
    $candidates += (Join-Path $env:ProgramFiles 'PdxNext\PdxLauncher.App.exe')
    if (${env:ProgramFiles(x86)}) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} 'PdxNext\PdxLauncher.App.exe')
    }

    # 用户可以在安装向导里自选目录，所以还要读两个安装模式的卸载项里的实际安装路径。
    $uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A2E3D5E1-5133-46BA-A846-1388A878E1A7}_is1'
    foreach ($hive in @('HKCU:', 'HKLM:')) {
        $key = Join-Path $hive $uninstallKey
        if (-not (Test-Path -LiteralPath $key)) {
            continue
        }

        $appPath = (Get-ItemProperty -LiteralPath $key -Name 'Inno Setup: App Path' -ErrorAction SilentlyContinue).'Inno Setup: App Path'
        if ($appPath) {
            $candidates += (Join-Path $appPath 'PdxLauncher.App.exe')
        }
    }
}

# 去重并丢掉空项，避免同一个路径被检查两遍。
$candidates = @($candidates | Where-Object { $_ } | Select-Object -Unique)

$found = 0
$failed = 0
foreach ($candidate in $candidates) {
    if (-not $candidate -or -not (Test-Path -LiteralPath $candidate)) {
        continue
    }

    $found++
    $actual = (Get-Item -LiteralPath $candidate).VersionInfo.FileVersion
    if ($actual -eq $expected) {
        Write-Host "OK   $candidate  FileVersion=$actual"
    } else {
        Write-Host "FAIL $candidate  expected=$expected  actual=$actual"
        $failed++
    }
}

if ($found -eq 0) {
    Write-Host "FAIL no installed PdxNext found. Install the new setup first."
    exit 1
}

if ($failed -gt 0) {
    exit 1
}

Write-Host "Installed version matches version.props ($version)."
exit 0
