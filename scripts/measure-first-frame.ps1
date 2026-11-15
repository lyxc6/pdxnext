# Requires: PdxLauncher.App Release build (dev.cmd build).
# Runs the launcher N times, reads the [first-frame] log line of each run,
# and prints per-phase medians. Read-only: it never touches repo sources.
param(
    [int]$Runs = 5,
    [string]$ExePath = '',
    [int]$TimeoutSeconds = 40,
    [string]$EnginePath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $repoRoot 'src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe'
}
if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "exe not found: $ExePath (run: cmd /c dev.cmd build)"
}
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
$exeDir = Split-Path -Parent $ExePath
$logPath = Join-Path $exeDir 'data\logs\pdxnext.log'
# 日志里的首帧明细标记（中文，与 HomePage / MainWindow 里的 [首帧明细] 一致）。
$marker = '[首帧明细]'

# Phases we care about, expressed as (from -> to) pairs over the milestone names.
$phases = @(
    @{ Name = 'shell-ready -> home-page-created'; From = 'shell-ready'; To = 'home-page-created' },
    @{ Name = 'home-page-created -> theme-ready'; From = 'home-page-created'; To = 'home-page-theme-ready' },
    @{ Name = 'home-page-loaded -> theme-ready'; From = 'home-page-loaded'; To = 'home-page-theme-ready' },
    @{ Name = 'backdrop-decode-start -> decoded'; From = 'backdrop-decode-start'; To = 'backdrop-decoded' },
    @{ Name = 'backdrop-decoded -> painted'; From = 'backdrop-decoded'; To = 'backdrop-painted' },
    @{ Name = 'app-start -> first-frame'; From = 'app-start'; To = 'first-frame' },
    @{ Name = 'app-start -> backdrop-painted'; From = 'app-start'; To = 'backdrop-painted' }
)

function Stop-Launcher {
    Get-Process -Name 'PdxLauncher.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 900
}

function Get-FirstFrameValues {
    param([long]$StartOffset, [int]$Timeout)

    $deadline = (Get-Date).AddSeconds($Timeout)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $logPath) {
            $fs = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
            try {
                if ($fs.Length -gt $StartOffset) {
                    $fs.Seek($StartOffset, 'Begin') | Out-Null
                    $reader = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
                    $text = $reader.ReadToEnd()
                }
                else {
                    $text = ''
                }
            }
            finally {
                $fs.Dispose()
            }

            if ($text.Contains($marker)) {
                $lines = @($text -split "`r?`n" | Where-Object { $_.Contains($marker) -and $_.Contains('backdrop-painted') })
                if ($lines.Count -gt 0) {
                    $line = $lines[$lines.Count - 1]
                    $payload = $line.Substring($line.IndexOf($marker) + $marker.Length)
                    $values = @{}
                    foreach ($pair in ($payload -split '\|')) {
                        $kv = $pair.Trim() -split '=', 2
                        if ($kv.Count -eq 2) {
                            $values[$kv[0]] = [double]$kv[1]
                        }
                    }
                    return $values
                }
            }
        }

        Start-Sleep -Milliseconds 150
    }

    return $null
}

$samples = @()
for ($i = 1; $i -le $Runs; $i++) {
    Stop-Launcher

    if (-not (Test-Path -LiteralPath $logPath)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null
    }
    $offset = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }

    $proc = Start-Process -FilePath $ExePath -WorkingDirectory $exeDir -PassThru
    $values = Get-FirstFrameValues -StartOffset $offset -Timeout $TimeoutSeconds
    if ($null -eq $values) {
        Write-Host ("run {0}: timeout, no first-frame line" -f $i) -ForegroundColor Yellow
    }
    else {
        $values['run'] = $i
        $samples += , $values
        Write-Host ("run {0}: first-frame={1} ms, backdrop-painted={2} ms" -f $i, $values['first-frame'], $values['backdrop-painted'])
    }

    if ($null -ne $proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 900
}
Stop-Launcher

Write-Host ''
if ($samples.Count -eq 0) {
    throw 'no sample collected'
}

function Get-Median {
    param([double[]]$Values)
    $sorted = $Values | Sort-Object
    $mid = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$mid] }
    return ([math]::Round(($sorted[$mid - 1] + $sorted[$mid]) / 2.0))
}

Write-Host ('median of {0} run(s):' -f $samples.Count)
$rows = @()
foreach ($phase in $phases) {
    $deltas = @()
    foreach ($sample in $samples) {
        if ($sample.ContainsKey($phase.From) -and $sample.ContainsKey($phase.To)) {
            $deltas += ($sample[$phase.To] - $sample[$phase.From])
        }
    }
    if ($deltas.Count -gt 0) {
        $rows += [pscustomobject]@{
            Phase = $phase.Name
            MedianMs = Get-Median -Values ([double[]]$deltas)
            MinMs = ($deltas | Measure-Object -Minimum).Minimum
            MaxMs = ($deltas | Measure-Object -Maximum).Maximum
        }
    }
}

$rows | Format-Table -AutoSize
