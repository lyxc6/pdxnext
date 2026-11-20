# PdxNext 进程查询/结束助手：按 exe 路径区分「本目录」与「其它目录」的副本。
#
# 为什么要它：以前 dev.cmd / start.cmd / status.cmd 与各验收脚本一律用
# tasklist / Get-Process 按进程名匹配 PdxLauncher.App.exe，于是同一台机器上装在别的盘
# （例如 D:\app\PdxNext）的那份会被一起统计/一起杀掉。自动化跑一次就会顺手关掉用户
# 正在用的安装版，DEV_PID 也可能报成别人的 PID。
#
# 用法：
#   pwsh -File scripts/pdx-app-process.ps1 -Action status -Exe <绝对路径>
#   pwsh -File scripts/pdx-app-process.ps1 -Action stop   -Exe <绝对路径>
#   pwsh -File scripts/pdx-app-process.ps1 -Action wait   -Exe <绝对路径> [-Since <UTC tick>]
#
# 输出是给 cmd 的 for /f 取值的 KEY=VALUE 行（全部 ASCII 键名）：
#   LOCAL=n          与本目录 exe 完全同路径的进程数
#   LOCAL_PIDS=a,b   上面这些的 PID（逗号分隔）
#   OTHER=n          其它路径的同名进程数（读不到 Path 的也计入这类）
#   OTHER_PIDS=a,b   同上
#   STOPPED=n        stop 实际结束的本目录进程数
#   PID=n            wait 找到的本目录进程 PID；没等到则为 0
#   STARTED=1|0      wait 是否在超时内等到
#
# 注意：绝不按进程名 kills。Path 读不到（其它用户、高完整性级别、已退出）时归入 OTHER，
# 只报告不动手 —— 宁漏杀不误杀。

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('status', 'stop', 'wait')]
    [string]$Action,

    [string]$Exe,
    [string]$ExeDir,
    [int]$TimeoutSeconds = 12
)

$ErrorActionPreference = 'Continue'

$processName = 'PdxLauncher.App'

function Resolve-TargetPath {
    param([string]$Direct, [string]$Directory)
    if (-not [string]::IsNullOrWhiteSpace($Direct)) {
        return [System.IO.Path]::GetFullPath($Direct)
    }
    if (-not [string]::IsNullOrWhiteSpace($Directory)) {
        return [System.IO.Path]::GetFullPath((Join-Path $Directory "$processName.exe"))
    }
    return $null
}

$target = Resolve-TargetPath -Direct $Exe -Directory $ExeDir
if (-not $target) {
    Write-Output 'LOCAL=0'
    Write-Output 'LOCAL_PIDS='
    Write-Output 'OTHER=0'
    Write-Output 'OTHER_PIDS='
    Write-Output 'STOPPED=0'
    Write-Output 'PID=0'
    Write-Output 'STARTED=0'
    exit 2
}

function Get-Candidates {
    $result = @()
    foreach ($p in @(Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { $path = $null }
        $result += [pscustomobject]@{ Id = $p.Id; Path = $path }
    }
    return $result
}

function Split-ByTarget {
    param($Candidates, [string]$TargetPath)
    $local = @()
    $other = @()
    foreach ($c in $Candidates) {
        if ($c.Path -and [string]::Equals($c.Path, $TargetPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $local += $c
        }
        else {
            $other += $c
        }
    }
    return [pscustomobject]@{ Local = $local; Other = $other }
}

function Emit-Counts {
    param($Split, [int]$Stopped = 0)
    Write-Output ('LOCAL=' + $Split.Local.Count)
    Write-Output ('LOCAL_PIDS=' + (($Split.Local | ForEach-Object { $_.Id }) -join ','))
    Write-Output ('OTHER=' + $Split.Other.Count)
    Write-Output ('OTHER_PIDS=' + (($Split.Other | ForEach-Object { $_.Id }) -join ','))
    Write-Output ('STOPPED=' + $Stopped)
}

switch ($Action) {
    'status' {
        Emit-Counts -Split (Split-ByTarget -Candidates (Get-Candidates) -TargetPath $target)
        Write-Output 'PID=0'
        Write-Output 'STARTED=0'
    }

    'stop' {
        $stopped = 0
        $split = Split-ByTarget -Candidates (Get-Candidates) -TargetPath $target
        foreach ($c in $split.Local) {
            try {
                Stop-Process -Id $c.Id -Force -ErrorAction Stop
                $stopped++
            }
            catch {
                # 进程可能刚好自己退了；不计入 stopped。
            }
        }

        # 等句柄真正释放：运行中的 exe 会锁 PdxLauncher.Core.dll，紧接着就构建会失败。
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            if (-not (Get-Process -Name $processName -ErrorAction SilentlyContinue)) { break }
            $remain = @(Get-Candidates | Where-Object {
                    $_.Path -and [string]::Equals($_.Path, $target, [System.StringComparison]::OrdinalIgnoreCase) })
            if ($remain.Count -eq 0) { break }
            Start-Sleep -Milliseconds 200
        }

        Emit-Counts -Split (Split-ByTarget -Candidates (Get-Candidates) -TargetPath $target) -Stopped $stopped
        Write-Output 'PID=0'
        Write-Output 'STARTED=0'
    }

    'wait' {
        $found = $null
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            $split = Split-ByTarget -Candidates (Get-Candidates) -TargetPath $target
            if ($split.Local.Count -gt 0) {
                $found = $split.Local | Select-Object -First 1
                break
            }
            Start-Sleep -Milliseconds 300
        }

        $split = Split-ByTarget -Candidates (Get-Candidates) -TargetPath $target
        Emit-Counts -Split $split
        if ($found) {
            Write-Output ('PID=' + $found.Id)
            Write-Output 'STARTED=1'
        }
        else {
            Write-Output 'PID=0'
            Write-Output 'STARTED=0'
        }
    }
}

exit 0
