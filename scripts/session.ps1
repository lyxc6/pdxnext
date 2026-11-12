#requires -version 5.1
<#
.SYNOPSIS
    一个对话一个分支：开分支 / 合回 / 查看。

.DESCRIPTION
    本仓库的约定是「每个新对话一个 sess/YYYYMMDD-<slug> 分支，从对话开始时的当前分支拉出，
    做完 --no-ff 合回那一支」。这个脚本把三步都代劳，避免只写在文档里没人执行。

        pwsh -File scripts/session.ps1 open <slug> [-From <branch>] [-Force]
        pwsh -File scripts/session.ps1 close [-Message <文字>] [-Into <branch>] [-SkipVerify] [-AllowDirty]
        pwsh -File scripts/session.ps1 list

    活跃版本分支记在仓库根 `integration-branch`（多行，每行一个 `ver/x.y.z`；空行与 # 注释忽略）。
    权威副本取 `main` 上的 integration-branch，各分支工作区里的副本只是快照。open 默认要求当前
    就站在清单里的某个活跃版本分支上；清单为空、或从 main / 已冻结分支起时加 -Force，并在汇报里
    写明原因（只用于引导、发版或从 tag 起紧急修复）。

    open   ：建 sess/YYYYMMDD-<slug>。工作区已有未提交改动时，改动跟着带到新分支并提交成
             wip(session) 基线，起始分支立刻变干净。基线记在 git config branch.<name>.pdxBase。
    close  ：校验基线仍在活跃清单 -> 构建 + 自检（纯 .md 改动自动跳过，-SkipVerify 强制跳过）-> 切回
             目标版本分支 -> git merge --no-ff -> 删除对话分支。基线已冻结/未登记时直接拒绝；
             冲突时中止合并并停在对话分支上。
    list   ：列出所有对话分支、基线、领先提交数。

    退出码：0 成功，1 用法/环境错误，2 合并冲突，3 工作区脏，4 构建或自检失败，5 分支仍被 worktree 占用。

.NOTES
    ASCII 标识（SESSION_BRANCH= / SESSION_BASE= / SESSION_WIP= / SESSION_MERGED=）供 agent 解析。
    丢弃未完成的分支是破坏性操作，脚本不提供；按用户明确指令手工执行：
        git switch <目标版本分支>; git branch -D sess/...
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'list',

    [Parameter(Position = 1)]
    [string]$Name,

    [string]$Message,
    [string]$Into,
    [string]$From,
    [switch]$SkipVerify,
    [switch]$AllowDirty,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# git 输出与提交信息都是 UTF-8；控制台默认是 GBK（936），不改这里中文字面会显示成乱码。
try {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [Console]::OutputEncoding = $utf8
    $OutputEncoding = $utf8
} catch {
    # 没有控制台（重定向 / CI）时设置会失败，忽略。
}

function Write-Step([string]$Text) {
    Write-Host $Text -ForegroundColor Cyan
}

function Write-Warn([string]$Text) {
    Write-Host $Text -ForegroundColor Yellow
}

function Write-Fail([string]$Text) {
    Write-Host $Text -ForegroundColor Red
}

function Assert-Git {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Fail '[错误] 找不到 git。'
        exit 1
    }
    if (-not (Test-Path (Join-Path $root '.git'))) {
        Write-Fail "[错误] 不是 git 仓库：$root"
        exit 1
    }
}

# 运行 git 并把输出打在屏幕上，返回退出码。
function Invoke-Git {
    param(
        [string[]]$GitArgs,
        [switch]$Quiet
    )
    if (-not $Quiet) {
        Write-Host ('  git ' + ($GitArgs -join ' ')) -ForegroundColor DarkGray
    }
    $raw = @(& git -C $root @GitArgs 2>&1)
    $code = $LASTEXITCODE
    if (-not $Quiet) {
        foreach ($line in $raw) {
            if ($null -ne $line) { Write-Host "  $line" }
        }
    }
    return $code
}

# 运行 git 并只返回输出文本；失败时返回 $null。
function Get-GitText {
    param([string[]]$GitArgs)
    $raw = @(& git -C $root @GitArgs 2>$null)
    if ($LASTEXITCODE -ne 0) { return $null }
    return (($raw | ForEach-Object { "$_" }) -join "`n").Trim()
}

function Get-CurrentBranch {
    $out = Get-GitText @('symbolic-ref', '--quiet', '--short', 'HEAD')
    if (-not $out) { return $null }
    return $out
}

function Test-BranchExists([string]$BranchName) {
    & git -C $root rev-parse --verify --quiet "refs/heads/$BranchName" > $null 2>&1
    return ($LASTEXITCODE -eq 0)
}

function Test-RemoteBranchExists([string]$BranchName) {
    & git -C $root rev-parse --verify --quiet "refs/remotes/origin/$BranchName" > $null 2>&1
    return ($LASTEXITCODE -eq 0)
}

# 基线必须是本地分支（要往里 merge）。本地没有但 origin 有时，建一条跟踪分支。
function Initialize-LocalBranch([string]$BranchName) {
    if (Test-BranchExists $BranchName) { return $true }
    if (-not (Test-RemoteBranchExists $BranchName)) { return $false }
    Write-Warn "本地没有 $BranchName，先建本地跟踪分支 origin/$BranchName"
    $code = Invoke-Git -GitArgs @('branch', '--track', $BranchName, "origin/$BranchName")
    return ($code -eq 0)
}

# 返回 status --porcelain 的每一行；已忽略的文件不算。
function Get-DirtyLines {
    $raw = @(& git -C $root status --porcelain -uall 2>$null)
    return @($raw | Where-Object { $_ -and "$_".Trim() })
}

function Get-BranchBase([string]$BranchName) {
    $base = & git -C $root config --local --get "branch.$BranchName.pdxBase" 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ("$base").Trim()
}

# 活跃版本分支的权威来源：main 上的 integration-branch（多行，每行一个分支）。各分支工作区里
# 的副本只是继承快照，不参与校验。空行与 # 注释忽略。
function Get-IntegrationBranches {
    $raw = @(& git -C $root show 'main:integration-branch' 2>$null)
    if ($LASTEXITCODE -eq 0) {
        # main 上有这个文件就以它为准；空文件表示「没有活跃分支」，不回退到分支工作区的旧副本。
        $content = (($raw | ForEach-Object { "$_" }) -join "`n")
    } else {
        $file = Join-Path $root 'integration-branch'
        if (-not (Test-Path $file)) { return @() }
        $content = Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue
    }
    if (-not $content) { return @() }
    return @(
        ($content -split "`n") |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and -not $_.StartsWith('#') }
    )
}

# 该分支是否还被「别的」worktree 检出；返回 worktree 路径数组。
# 当前工作树不算——close 本来就在对话分支自己的工作树里执行。
function Get-WorktreesForBranch([string]$BranchName) {
    $self = (Get-GitText @('rev-parse', '--show-toplevel'))
    if ($self) { $self = $self -replace '\\', '/' -replace '/+$', '' }
    $raw = @(& git -C $root worktree list --porcelain 2>$null)
    $paths = @()
    $current = $null
    foreach ($line in $raw) {
        $text = "$line"
        if ($text -like 'worktree *') { $current = $text.Substring(9); continue }
        if ($text -eq "branch refs/heads/$BranchName" -and $current) {
            $normalized = $current -replace '\\', '/' -replace '/+$', ''
            if (-not $self -or $normalized -ne $self) { $paths += $current }
        }
    }
    return $paths
}

# 只改文档的对话不跑构建（docs/AGENT-WORKFLOW.md §5）。
function Test-DocsOnly([string]$Base, [string]$BranchName) {
    $files = @(& git -C $root diff --name-only "$Base...$BranchName" 2>$null)
    if ($files.Count -eq 0) { return $true }
    foreach ($file in $files) {
        $text = "$file"
        if ($text -and $text -notmatch '\.md$') { return $false }
    }
    return $true
}

function ConvertTo-Slug([string]$Text) {
    $slug = $Text.ToLowerInvariant()
    $slug = [regex]::Replace($slug, '[^a-z0-9]+', '-')
    $slug = $slug.Trim('-')
    if ($slug.Length -gt 40) { $slug = $slug.Substring(0, 40).Trim('-') }
    return $slug
}

function Show-Usage {
    Write-Host @'
用法：
  pwsh -File scripts/session.ps1 open <slug> [-From <branch>] [-Force]
  pwsh -File scripts/session.ps1 close [-Message <文字>] [-Into <branch>] [-SkipVerify] [-AllowDirty]
  pwsh -File scripts/session.ps1 list

open  建 sess/YYYYMMDD-<slug>，基线是当前分支（或 -From 指定的分支）。
      当前必须站在 integration-branch 清单里的活跃版本分支上；清单为空、或从 main / 已冻结分支
      起时会被拒绝。方案外的 -Force 只用于引导、发版或从 tag 起紧急修复。
      工作区已有改动会跟随到新分支并提交成 wip(session) 基线。
close 校验基线仍在活跃清单 -> 构建 + 自检 -> --no-ff 合回目标版本分支，删除对话分支并切回。
      基线已冻结/未登记时拒绝；可用 -Into <活跃分支> 重定向。
      纯 .md 改动自动跳过构建；-SkipVerify 强制跳过。
list  列出活跃版本分支、对话分支、基线与领先提交数；冻结基线会被标记。
'@
}

function Invoke-Open {
    if (-not $Name) {
        Write-Fail '[错误] open 需要一个 slug，例如：session.ps1 open fix-dlc-artwork'
        exit 1
    }

    $slug = ConvertTo-Slug $Name
    if (-not $slug) {
        Write-Fail "[错误] slug 至少要有一个字母或数字：$Name"
        exit 1
    }

    $current = Get-CurrentBranch
    if (-not $current) {
        Write-Fail '[错误] 当前是 detached HEAD，先 git switch 到一个分支再开对话分支。'
        exit 1
    }
    if ($current -like 'sess/*') {
        Write-Fail "[错误] 已经在对话分支 $current 上。要开新对话，先 close，或 git switch 回起始分支。"
        exit 1
    }

    $integrationBranches = @(Get-IntegrationBranches)
    if ($integrationBranches.Count -eq 0) {
        if (-not $Force) {
            Write-Fail '[错误] integration-branch 里没有活跃版本分支。'
            Write-Host '先按 docs/VERSIONING.md §5.1 在 main 上登记，再从 main 创建 ver/x.y.z。'
            Write-Host '只有引导仓库时才使用 -Force。'
            exit 1
        }
        Write-Warn '[提醒] -Force：活跃版本分支清单为空，按引导状态继续。'
    } elseif ($current -notin $integrationBranches) {
        if (-not $Force) {
            Write-Fail "[错误] 当前分支是 $current，不在活跃版本分支清单里。"
            Write-Host ("活跃版本分支：{0}" -f ($integrationBranches -join ', '))
            Write-Host '先 git switch <活跃分支>，然后重跑 open。'
            Write-Host '已发布分支已冻结；-Force 只用于引导、发版或从 tag 起紧急修复，并在汇报里写明原因。'
            exit 1
        }
        Write-Warn "[提醒] -Force：从 $current 而不是活跃版本分支起对话分支。"
    }

    $base = $current
    if ($From) {
        if ((Get-DirtyLines).Count -gt 0) {
            Write-Fail '[错误] 工作区有未提交改动，不能从其它分支起分支；先处理改动，或去掉 -From。'
            exit 1
        }
        if (-not (Initialize-LocalBranch $From)) {
            Write-Fail "[错误] 基线分支不存在（本地和 origin 都没有）：$From"
            exit 1
        }
        $base = $From
    }

    $date = Get-Date -Format 'yyyyMMdd'
    $stem = "sess/$date-$slug"
    $branchName = $stem
    $index = 2
    while (Test-BranchExists $branchName) {
        $branchName = "$stem-$index"
        $index++
    }

    $dirty = @(Get-DirtyLines)
    Write-Step "从 $base 开对话分支 $branchName"

    if ($base -eq $current) {
        $code = Invoke-Git -GitArgs @('switch', '-c', $branchName)
    } else {
        $code = Invoke-Git -GitArgs @('switch', '-c', $branchName, $base)
    }
    if ($code -ne 0) {
        Write-Fail "[错误] git switch 失败（退出码 $code）。"
        exit 1
    }

    if ($dirty.Count -gt 0) {
        Write-Warn "工作区有 $($dirty.Count) 处未提交改动，提交成 wip 基线（不是交付内容）："
        $code = Invoke-Git -GitArgs @('add', '-A')
        if ($code -ne 0) {
            Write-Fail "[错误] git add -A 失败（退出码 $code）。分支 $branchName 已建好，改动仍在工作区。"
            exit 1
        }
        $wipMessage = "wip(session): 保留对话开始前已有的改动（基线 $base）"
        $code = Invoke-Git -GitArgs @('commit', '-m', $wipMessage)
        if ($code -ne 0) {
            Write-Fail "[错误] wip 提交失败（退出码 $code）。"
            exit 1
        }
    }

    & git -C $root config --local "branch.$branchName.pdxBase" $base
    if ($LASTEXITCODE -ne 0) {
        Write-Fail '[错误] 写入基线配置失败。'
        exit 1
    }

    if ($base -eq 'main') {
        Write-Warn '[提醒] 本对话从 main 起分支，收尾会合回 main。main 必须始终可发布。'
    }

    Write-Host ''
    Write-Host "对话分支就绪：$branchName" -ForegroundColor Green
    Write-Host '  - 本对话的所有提交都留在它上面，收尾前不往起始分支写任何东西。'
    Write-Host '  - 子 agent 不要自己建分支或切分支。'
    Write-Host '  - 收尾：pwsh -File scripts/session.ps1 close -Message "中文说明"'
    Write-Host ''
    Write-Host "SESSION_BRANCH=$branchName"
    Write-Host "SESSION_BASE=$base"
    Write-Host ("SESSION_INTEGRATION=" + ($integrationBranches -join ','))
    Write-Host "SESSION_WIP=$($dirty.Count)"
}

function Invoke-Close {
    Assert-Git

    $branchName = Get-CurrentBranch
    if (-not $branchName) {
        Write-Fail '[错误] 当前是 detached HEAD，无法 close。'
        exit 1
    }
    if ($branchName -notlike 'sess/*') {
        Write-Fail "[错误] 当前分支是 $branchName，不是对话分支（sess/*）。请在对话分支上执行 close。"
        exit 1
    }

    $base = $Into
    if (-not $base) { $base = Get-BranchBase $branchName }
    if (-not $base) {
        Write-Fail '[错误] 找不到这个分支的基线，请用 -Into <分支> 指定。'
        exit 1
    }
    if (-not (Initialize-LocalBranch $base)) {
        Write-Fail "[错误] 基线分支不存在（本地和 origin 都没有）：$base"
        exit 1
    }
    if ($base -eq $branchName) {
        Write-Fail '[错误] 基线与对话分支相同。'
        exit 1
    }

    $integrationBranches = @(Get-IntegrationBranches)
    if ($integrationBranches.Count -eq 0) {
        Write-Warn '[提醒] integration-branch 里没有活跃版本分支，按引导状态合回基线。'
    } elseif ($base -notin $integrationBranches) {
        Write-Fail "[错误] 基线 $base 已冻结或不在活跃版本分支清单里，不能合回。"
        Write-Host ("活跃版本分支：{0}" -f ($integrationBranches -join ', '))
        Write-Host '如果这次对话还有效，用 close -Into <活跃分支> 重定向；否则从活跃分支重开对话。'
        exit 1
    }

    $worktrees = @(Get-WorktreesForBranch $branchName)
    if ($worktrees.Count -gt 0) {
        Write-Fail "[错误] 对话分支 $branchName 仍被 worktree 占用："
        foreach ($path in $worktrees) { Write-Host "  $path" }
        Write-Host '先 git worktree remove <路径>（或删掉该 worktree）再 close。'
        exit 5
    }

    $dirty = @(Get-DirtyLines)
    if ($dirty.Count -gt 0 -and -not $AllowDirty) {
        Write-Fail "工作区有 $($dirty.Count) 处未提交改动，先提交再 close："
        foreach ($line in $dirty) { Write-Host "  $line" }
        Write-Host '确实要带着未提交改动合并，用 -AllowDirty。'
        exit 3
    }

    $ahead = [int](Get-GitText @('rev-list', '--count', "$base..$branchName"))
    $shortBranch = $branchName -replace '^sess/\d{8}-', ''

    $needVerify = -not $SkipVerify
    if ($needVerify) {
        if (Test-DocsOnly $base $branchName) {
            Write-Host '本次只改文档（*.md），按规范跳过构建与自检。'
            $needVerify = $false
        }
    }

    if ($needVerify) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            Write-Fail '[错误] 找不到 dotnet，无法构建。'
            exit 4
        }
        Write-Step '构建（-m:1）...'
        Push-Location $root
        try {
            & dotnet build 'PdxLauncher.sln' -c Release -m:1
            $buildCode = $LASTEXITCODE
            if ($buildCode -ne 0) {
                Write-Fail "[失败] 构建退出码 $buildCode。不合并，分支 $branchName 保留。"
                exit 4
            }

            Write-Step '自检...'
            $checks = 'tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll'
            $raw = @(& dotnet $checks 2>&1)
            $checkCode = $LASTEXITCODE
            $text = ($raw | ForEach-Object { "$_" }) -join "`n"
            Write-Host $text
            if ($checkCode -ne 0 -or $text -notmatch '失败:\s*0') {
                Write-Fail "[失败] 自检未通过（退出码 $checkCode）。不合并，分支 $branchName 保留。"
                exit 4
            }
        } finally {
            Pop-Location
        }
    }

    if ($ahead -eq 0) {
        Write-Warn "分支 $branchName 相对 $base 没有新提交，直接切回并删除。"
        $code = Invoke-Git -GitArgs @('switch', $base)
        if ($code -ne 0) { exit 1 }
        Invoke-Git -GitArgs @('branch', '-d', $branchName) | Out-Null
        & git -C $root config --local --unset "branch.$branchName.pdxBase" 2>$null | Out-Null
        Write-Host "SESSION_MERGED="
        return
    }

    if (-not $Message) {
        $Message = "对话：$shortBranch"
    }
    $mergeMessage = "merge: $Message（$branchName）"

    Write-Step "切回 $base 并 --no-ff 合并"
    $code = Invoke-Git -GitArgs @('switch', $base)
    if ($code -ne 0) {
        Write-Fail "[错误] 切回 $base 失败（退出码 $code）。"
        exit 1
    }

    $code = Invoke-Git -GitArgs @('merge', '--no-ff', '-m', $mergeMessage, $branchName)
    if ($code -ne 0) {
        Invoke-Git -GitArgs @('merge', '--abort') -Quiet | Out-Null
        Invoke-Git -GitArgs @('switch', $branchName) -Quiet | Out-Null
        Write-Fail '合并冲突，已中止合并，仍停在对话分支上。'
        Write-Host "先在这个分支上 git merge $base 并解决冲突、提交，再重跑 close。"
        exit 2
    }

    $code = Invoke-Git -GitArgs @('branch', '-d', $branchName)
    if ($code -ne 0) {
        Write-Warn "合并成功，但删除分支失败（退出码 $code）；可稍后 git branch -d $branchName"
    }
    & git -C $root config --local --unset "branch.$branchName.pdxBase" 2>$null | Out-Null

    $head = (Get-GitText @('rev-parse', '--short', 'HEAD'))
    Write-Host ''
    Write-Host "已合回 $base：$mergeMessage" -ForegroundColor Green
    Write-Host "SESSION_MERGED=$head"
}

function Invoke-List {
    Assert-Git

    $integrationBranches = @(Get-IntegrationBranches)
    if ($integrationBranches.Count -gt 0) {
        Write-Host ("当前活跃版本分支：" + ($integrationBranches -join '  '))
    } else {
        Write-Warn '[提醒] integration-branch 里没有活跃版本分支。'
    }

    $refs = @(& git -C $root for-each-ref 'refs/heads/sess' --format='%(refname:short)' 2>$null)
    $refs = @($refs | Where-Object { $_ })
    if ($refs.Count -eq 0) {
        Write-Host '没有进行中的对话分支。'
        return
    }

    $current = Get-CurrentBranch
    Write-Host '会话分支                                                  基线        领先  备注'
    foreach ($branchName in $refs) {
        $base = Get-BranchBase $branchName
        if (-not $base) { $base = '?' }
        $ahead = Get-GitText @('rev-list', '--count', "$base..$branchName")
        if ($null -eq $ahead) { $ahead = '?' }
        $notes = @()
        if ($integrationBranches.Count -gt 0 -and $base -ne '?' -and $base -notin $integrationBranches) {
            $notes += '[冻结/未登记]'
        }
        $worktrees = @(Get-WorktreesForBranch $branchName)
        foreach ($path in $worktrees) { $notes += "worktree=$path" }
        if ($branchName -eq $current) { $notes += '当前' }
        $noteText = ($notes -join ' ')
        Write-Host ("{0,-58} {1,-11} {2,4}  {3}" -f $branchName, $base, $ahead, $noteText)
    }
}

Assert-Git

switch ($Command.ToLowerInvariant()) {
    'open' { Invoke-Open }
    'close' { Invoke-Close }
    'list' { Invoke-List }
    'help' { Show-Usage }
    '-h' { Show-Usage }
    '--help' { Show-Usage }
    '/?' { Show-Usage }
    default {
        Write-Fail "未知子命令：$Command"
        Show-Usage
        exit 1
    }
}
