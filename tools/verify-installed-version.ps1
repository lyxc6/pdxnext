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
}

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
