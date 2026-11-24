#requires -version 5.1
<#
.SYNOPSIS
    一个对话一个分支：开分支 / 合回 / 查看（支持并行对话各占一棵工作树）。

.DESCRIPTION
    本仓库的约定是「每个新对话一个 sess/YYYYMMDD-<slug> 分支，从对话开始时的活跃版本分支拉出，
    做完 --no-ff 合回那一支」。这个脚本把三步都代劳，避免只写在文档里没人执行。

        pwsh -File scripts/session.ps1 open <slug> [-Worktree] [-CarryDirty] [-From <branch>] [-Force]
        pwsh -File scripts/session.ps1 close [-Message <文字>] [-Into <branch>] [-SkipVerify] [-AllowDirty]
        pwsh -File scripts/session.ps1 list

    并行对话（同一台机器上同时有两条以上对话在写代码）不需要互相等待：每条对话用
    `open <slug> -Worktree` 在 `.agents/worktrees/<slug>` 里独立检出，各自的 bin/obj 互不遮挡，
    主工作树继续留在活跃版本分支上；合并仍然串行 —— 同一时刻只有一条对话在执行 close。

    活跃版本分支记在仓库根 `integration-branch`（多行，每行一个 `ver/x.y.z`；空行与 # 注释忽略）。
    权威副本取 `main` 上的 integration-branch，各分支工作区里的副本只是快照。open 要求**基线**
    （默认是当前分支；-From 指定，或 -Worktree 从 sess 分支起时用它的基线）落在清单里；清单为空、
    或基线已冻结时加 -Force，并在汇报里写明原因（只用于引导、发版或从 tag 起紧急修复）。

    open   ：建 sess/YYYYMMDD-<slug>，基线记在 git config branch.<name>.pdxBase。
             默认在当前工作树上切过去；工作区有未提交改动时**直接拒绝**（退出 3），确需带走旧改动
             才加 -CarryDirty（提交成 wip(session) 基线）。
             -Worktree 不碰当前工作区，在 .agents/worktrees/<slug> 新建一棵树（路径记在 pdxWorktree）。
    close  ：校验基线仍在活跃清单 -> 构建 + 自检（纯 .md 改动自动跳过，-SkipVerify 强制跳过）->
             在「检出该版本分支的那棵工作树」里 git merge --no-ff -> 删除对话分支。
             那棵树不是当前树时（并行对话的常态）就在它的目录里合并，所以不必等别人先收尾；
             它自己有未提交改动时拒绝（退出 6），避免把别人的半成品混进合并。
             基线已冻结/未登记时拒绝；冲突时中止合并并停在对话分支上。
    list   ：列出所有对话分支、基线、领先提交数、所属工作树与那棵树是否干净。

    退出码：0 成功，1 用法/环境错误，2 合并冲突，3 工作区脏，4 构建或自检失败，
             5 分支仍被 worktree 占用，6 版本分支被另一条对话占用且那棵树有未提交改动。

.NOTES
    ASCII 标识（SESSION_BRANCH= / SESSION_BASE= / SESSION_INTEGRATION= / SESSION_WIP= /
    SESSION_WORKTREE= / SESSION_BASE_OWNER= / SESSION_MERGED=）供 agent 解析。
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
    [switch]$Worktree,
    [switch]$CarryDirty,
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

# 运行 git 并把输出打在屏幕上，返回退出码。-In 指定在哪棵工作树里执行（并行对话会用到）。
function Invoke-Git {
    param(
        [string[]]$GitArgs,
        [string]$In,
        [switch]$Quiet
    )
    $where = if ($In) { $In } else { $root }
    if (-not $Quiet) {
        $prefix = if ($In) { "[$In] " } else { '' }
        Write-Host ('  ' + $prefix + 'git ' + ($GitArgs -join ' ')) -ForegroundColor DarkGray
    }
    $raw = @(& git -C $where @GitArgs 2>&1)
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
    param(
        [string[]]$GitArgs,
        [string]$In
    )
    $where = if ($In) { $In } else { $root }
    $raw = @(& git -C $where @GitArgs 2>$null)
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

# 当前工作树的顶层目录（规范化成 git 用的斜杠形式）。
function Get-SelfTopLevel {
    $self = (Get-GitText @('rev-parse', '--show-toplevel'))
    if (-not $self) { return $null }
    return ($self -replace '\\', '/' -replace '/+$', '')
}

# 当前是不是「链接出来的」工作树（.git 是文件、指向 <common>/.git/worktrees/<name>）。
# 链接树里的分支一旦被删，git 会拒绝（cannot delete branch ... used by worktree），所以 close 要先 detach。
function Test-LinkedWorktree {
    $gitDir = (Get-GitText @('rev-parse', '--git-dir'))
    if (-not $gitDir) { return $false }
    return ($gitDir -match '(?i)/worktrees/')
}

# 检出某分支的那棵工作树（含当前树）。没有树检出它时返回 $null。
function Get-WorktreeOwningBranch([string]$BranchName) {
    $raw = @(& git -C $root worktree list --porcelain 2>$null)
    $current = $null
    foreach ($line in $raw) {
        $text = "$line"
        if ($text -like 'worktree *') { $current = $text.Substring(9); continue }
        if ($text -eq "branch refs/heads/$BranchName" -and $current) { return $current }
    }
    return $null
}

# 解析 git 的 "already used by worktree at '<path>'" 报错，给出该树路径（拿不到时返回 $null）。
function Get-CheckoutConflictPath([string[]]$GitOutput) {
    foreach ($line in $GitOutput) {
        $text = "$line"
        if ($text -match "already used by worktree at '([^']+)'") { return $Matches[1] }
        if ($text -match "already checked out at '([^']+)'") { return $Matches[1] }
    }
    return $null
}

# 指定工作树里的未提交改动条数；目录不存在时返回 $null。
function Get-DirtyCountAt([string]$WorkTreePath) {
    if (-not $WorkTreePath -or -not (Test-Path -LiteralPath $WorkTreePath)) { return $null }
    $raw = @(& git -C $WorkTreePath status --porcelain -uall 2>$null)
    if ($LASTEXITCODE -ne 0) { return $null }
    return @($raw | Where-Object { $_ -and "$_".Trim() }).Count
}

# 分支对应的对话工作树路径（open -Worktree 时记进 git config）。
function Get-BranchWorktree([string]$BranchName) {
    $path = & git -C $root config --local --get "branch.$BranchName.pdxWorktree" 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ("$path").Trim()
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
  pwsh -File scripts/session.ps1 open <slug> [-Worktree] [-CarryDirty] [-From <branch>] [-Force]
  pwsh -File scripts/session.ps1 close [-Message <文字>] [-Into <branch>] [-SkipVerify] [-AllowDirty]
  pwsh -File scripts/session.ps1 list

open  建 sess/YYYYMMDD-<slug>，基线是活跃版本分支（当前分支 / 它的 pdxBase / -From 指定的分支）。
      基线必须是 integration-branch 清单里的分支；清单为空、或基线已冻结时会被拒绝（-Force 例外，
      只用于引导、发版或从 tag 起紧急修复）。
      工作区有未提交改动时直接拒绝（退出 3）：先提交，或加 -Worktree 另开一棵树，确需带走才用 -CarryDirty。
      -Worktree 不碰当前工作区，在 .agents/worktrees/<slug> 里检出这条对话分支；并行对话用它。
close 校验基线仍在活跃清单 -> 构建 + 自检 -> --no-ff 合回目标版本分支（在与它同名的那棵工作树里合并，
      不必等占用它的另一次对话先收尾），删除对话分支。
      基线已冻结/未登记时拒绝；可用 -Into <活跃分支> 重定向。
      那棵工作树自己有未提交改动时拒绝（退出 6），避免把别人的半成品混进合并。
      纯 .md 改动自动跳过构建；-SkipVerify 强制跳过。
list  列出活跃版本分支、对话分支、基线与领先提交数、各自的工作树与干净与否。
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

    # 基线：从 sess 分支起（-Worktree 并行时常见）就用它的基线，否则用当前分支。
    $baseFromSess = $null
    if ($current -like 'sess/*') {
        if (-not $Worktree) {
            Write-Fail "已经在对话分支 $current 上。"
            Write-Host '  (1) 收尾这条对话：pwsh -File scripts/session.ps1 close -Message "中文说明"'
            Write-Host '  (2) 另开一条并行对话（不动当前工作区）：pwsh -File scripts/session.ps1 open <slug> -Worktree'
            exit 1
        }
        $baseFromSess = Get-BranchBase $current
        if (-not $baseFromSess) {
            Write-Fail "当前在对话分支 $current 上，但找不到它的基线（pdxBase）。"
            Write-Host '先 git switch 回活跃版本分支，再用 -Worktree，或补上基线：'
            Write-Host "  git config --local branch.$current.pdxBase <活跃分支>"
            exit 1
        }
    }

    $integrationBranches = @(Get-IntegrationBranches)
    $base = if ($baseFromSess) { $baseFromSess } else { $current }
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

    if ($integrationBranches.Count -eq 0) {
        if (-not $Force) {
            Write-Fail '[错误] integration-branch 里没有活跃版本分支。'
            Write-Host '先按 docs/VERSIONING.md §5.1 在 main 上登记，再从 main 创建 ver/x.y.z。'
            Write-Host '只有引导仓库时才使用 -Force。'
            exit 1
        }
        Write-Warn '[提醒] -Force：活跃版本分支清单为空，按引导状态继续。'
    } elseif ($base -notin $integrationBranches) {
        if (-not $Force) {
            Write-Fail "[错误] 基线是 $base，不在活跃版本分支清单里。"
            Write-Host ("活跃版本分支：{0}" -f ($integrationBranches -join ', '))
            Write-Host '先 git switch <活跃分支>，然后重跑 open。'
            Write-Host '已发布分支已冻结；-Force 只用于引导、发版或从 tag 起紧急修复，并在汇报里写明原因。'
            exit 1
        }
        Write-Warn "[提醒] -Force：从 $base 而不是活跃版本分支起对话分支。"
    }

    $date = Get-Date -Format 'yyyyMMdd'
    $stem = "sess/$date-$slug"
    $branchName = $stem
    $index = 2
    while (Test-BranchExists $branchName) {
        $branchName = "$stem-$index"
        $index++
    }

    $dirty = @()
    $worktreePath = $null
    if ($Worktree) {
        # 并行对话：新开一棵树，当前工作区一点都不动。
        $worktreePath = Join-Path $root (Join-Path '.agents' (Join-Path 'worktrees' $slug))
        if (Test-Path -LiteralPath $worktreePath) {
            Write-Fail "[错误] 工作树目录已存在：$worktreePath"
            Write-Host '换一个 slug，或先确认那棵树是不是上一次没清理的（git worktree list）。'
            exit 1
        }
    } else {
        $dirty = @(Get-DirtyLines)
        if ($dirty.Count -gt 0 -and -not $CarryDirty) {
            Write-Fail "工作区有 $($dirty.Count) 处未提交改动，不自动带走（那是别人的半成品，混进基线会很难拆）："
            foreach ($line in $dirty) { Write-Host "  $line" }
            Write-Host ''
            Write-Host '  三种出路：'
            Write-Host '    (1) 先提交/清理这些改动，再重跑 open'
            Write-Host '    (2) 加 -Worktree 另开一棵工作树，当前工作区不动'
            Write-Host '    (3) 确实要把它们带进这条对话的基线，加 -CarryDirty'
            exit 3
        }
    }

    Write-Step "从 $base 开对话分支 $branchName"
    if ($Worktree) {
        # git worktree add 会自己建目录；父目录由 git 负责，这里只需要一个绝对路径。
        $absPath = Join-Path $root (Join-Path '.agents' (Join-Path 'worktrees' $slug))
        $code = Invoke-Git -GitArgs @('worktree', 'add', '-b', $branchName, $absPath, $base)
        if ($code -ne 0) {
            Write-Fail "[错误] git worktree add 失败（退出码 $code）。"
            exit 1
        }
        $worktreePath = (Get-BranchWorktree $branchName)
        if (-not $worktreePath -or -not (Test-Path -LiteralPath $worktreePath)) { $worktreePath = $absPath }
        Write-Host "  对话工作树：$worktreePath" -ForegroundColor DarkGray
    } else {
        if ($base -eq $current) {
            $code = Invoke-Git -GitArgs @('switch', '-c', $branchName)
        } else {
            $code = Invoke-Git -GitArgs @('switch', '-c', $branchName, $base)
        }
        if ($code -ne 0) {
            Write-Fail "[错误] git switch 失败（退出码 $code）。"
            exit 1
        }
    }

    if ($CarryDirty -and -not $Worktree -and $dirty.Count -gt 0) {
        Write-Warn "工作区有 $($dirty.Count) 处未提交改动，按 -CarryDirty 提交成 wip 基线（不是交付内容）："
        $code = Invoke-Git -GitArgs @('add', '-A')
        if ($code -ne 0) {
            Write-Fail "[错误] git add -A 失败（退出码 $code）。分支 $branchName 已建好，改动仍在工作区。"
            exit 1
        }
        $carried = @($dirty | ForEach-Object { $_.Trim() })
        $listed = ($carried | Select-Object -First 10) -join '; '
        if ($carried.Count -gt 10) { $listed = $listed + " 等 $($carried.Count) 项" }
        $wipMessage = "wip(session): 保留对话开始前已有的改动（基线 $base）`n`n$listed"
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
    if ($Worktree) {
        & git -C $root config --local "branch.$branchName.pdxWorktree" $worktreePath
        if ($LASTEXITCODE -ne 0) {
            Write-Fail '[错误] 写入工作树配置失败。'
            exit 1
        }
    }

    if ($base -eq 'main') {
        Write-Warn '[提醒] 本对话从 main 起分支，收尾会合回 main。main 必须始终可发布。'
    }

    Write-Host ''
    Write-Host "对话分支就绪：$branchName" -ForegroundColor Green
    Write-Host '  - 本对话的所有提交都留在它上面，收尾前不往起始分支写任何东西。'
    Write-Host '  - 子 agent 不要自己建分支或切分支。'
    Write-Host '  - 收尾：pwsh -File scripts/session.ps1 close -Message "中文说明"'
    if ($Worktree) {
        Write-Host ''
        Write-Host "并行对话：本对话的工作目录是上面那棵工作树，构建与自检都要在那里跑：" -ForegroundColor Yellow
        Write-Host "  cd `"$worktreePath`""
        Write-Host '  收尾后清理：git worktree remove <上面那个路径>'
    }
    Write-Host ''
    Write-Host "SESSION_BRANCH=$branchName"
    Write-Host "SESSION_BASE=$base"
    Write-Host ("SESSION_INTEGRATION=" + ($integrationBranches -join ','))
    Write-Host "SESSION_WIP=$($dirty.Count)"
    if ($Worktree) { Write-Host "SESSION_WORKTREE=$worktreePath" }
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
            & dotnet build 'PdxLauncher.sln' -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false
            $buildCode = $LASTEXITCODE
            if ($buildCode -ne 0) {
                Write-Fail "[失败] 构建退出码 $buildCode。不合并，分支 $branchName 保留。"
                exit 4
            }

            Write-Step '自检...'
            $checks = 'tests\PdxLauncher.Checks\bin\Release\net11.0\PdxLauncher.Checks.dll'
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
        if (Test-LinkedWorktree) {
            # 链接树里不能直接 switch 到 base（base 可能正被别的树检出），detach 后删分支即可。
            Invoke-Git -GitArgs @('switch', '--detach', $base) -Quiet | Out-Null
        } else {
            $code = Invoke-Git -GitArgs @('switch', $base)
            if ($code -ne 0) { exit 1 }
        }
        Invoke-Git -GitArgs @('branch', '-d', $branchName) -Quiet | Out-Null
        & git -C $root config --local --unset "branch.$branchName.pdxBase" 2>$null | Out-Null
        & git -C $root config --local --unset "branch.$branchName.pdxWorktree" 2>$null | Out-Null
        Write-Host "SESSION_MERGED="
        return
    }

    if (-not $Message) {
        $Message = "对话：$shortBranch"
    }
    $mergeMessage = "merge: $Message（$branchName）"

    # 谁检出着基线分支，就在谁的目录里合并。并行对话时那可能是另一棵工作树，
    # 不必等那边收尾；但那棵树自己的未提交改动必须先走，否则会被卷进这次合并。
    $self = Get-SelfTopLevel
    $selfLinked = Test-LinkedWorktree
    $pdxWorktree = Get-BranchWorktree $branchName
    $owner = Get-WorktreeOwningBranch $base
    $mergeIn = $null

    if ($owner) {
        $ownerNorm = $owner -replace '\\', '/' -replace '/+$', ''
        $ownerDirty = Get-DirtyCountAt $owner
        if ($ownerDirty -eq $null) {
            Write-Fail "[错误] 检出 $base 的工作树不可读：$owner"
            exit 1
        }
        if ($ownerDirty -gt 0) {
            Write-Fail "$base 正被另一条对话占用，那棵工作树有 $ownerDirty 处未提交改动："
            Write-Host "  $owner"
            Write-Host '先把那边的改动提交到它自己的对话分支（或在那边处理掉），再重跑 close。'
            Write-Host '现在合并会把它的半成品一起卷进来，所以脚本不替你做。'
            Write-Host "  [那棵树的改动] git -C `"$owner`" status --porcelain -uall"
            Write-Host "  [那边在哪条分支上] git -C `"$owner`" rev-parse --abbrev-ref HEAD"
            exit 6
        }
        if ($self -and $ownerNorm -eq $self) {
            # 基线就检在当前树上：走原来的路径（先切过去再合并）。
            Write-Step "切回 $base 并 --no-ff 合并"
            $switchOutput = @(& git -C $root switch $base 2>&1)
            $code = $LASTEXITCODE
            foreach ($line in $switchOutput) { if ($null -ne $line) { Write-Host "  $line" } }
            if ($code -ne 0) {
                Write-Fail "[错误] 切回 $base 失败（退出码 $code）。"
                $conflictPath = Get-CheckoutConflictPath $switchOutput
                if ($conflictPath) {
                    Write-Host "$base 正被另一棵工作树检出：$conflictPath"
                    Write-Host '那是另一条对话的工作目录；合并应该在那里发生，或者用 -Into <活跃分支> 换目标。'
                }
                exit 1
            }
        } else {
            # 基线检在别的工作树上：直接在它的目录里合并，当前树不动。
            Write-Step "在另一棵工作树里合并（当前工作树不动）：$owner"
            $mergeIn = $owner
        }
    } else {
        # 没有任何树检出基线：当前树切过去合并（自检过，这时切换是合法的）。
        Write-Step "切回 $base 并 --no-ff 合并"
        $code = Invoke-Git -GitArgs @('switch', $base)
        if ($code -ne 0) {
            Write-Fail "[错误] 切回 $base 失败（退出码 $code）。"
            exit 1
        }
    }

    $code = Invoke-Git -GitArgs @('merge', '--no-ff', '-m', $mergeMessage, $branchName) -In $mergeIn
    if ($code -ne 0) {
        Invoke-Git -GitArgs @('merge', '--abort') -In $mergeIn -Quiet | Out-Null
        if (-not $mergeIn) { Invoke-Git -GitArgs @('switch', $branchName) -Quiet | Out-Null }
        Write-Fail '合并冲突，已中止合并，对话分支保留。'
        if ($mergeIn) {
            Write-Host "冲突发生在 $mergeIn（$base 所在的工作树）。"
            Write-Host "先在那里 git merge $branchName 并解决冲突、提交，再重跑 close。"
        } else {
            Write-Host "先在这个分支上 git merge $base 并解决冲突、提交，再重跑 close。"
        }
        exit 2
    }

    # 链接工作树不能留在 base 上：那会把 base 锁给这棵（马上要删掉的）树，主工作树之后切不过去。
    # 主工作树留在 base 上是历史行为，保持不变。
    if ($selfLinked) {
        Invoke-Git -GitArgs @('switch', '--detach', $base) -Quiet | Out-Null
    }

    $code = Invoke-Git -GitArgs @('branch', '-d', $branchName) -Quiet
    if ($code -ne 0) {
        Write-Warn "合并成功，但删除分支失败（退出码 $code）；可稍后 git branch -d $branchName"
    }
    & git -C $root config --local --unset "branch.$branchName.pdxBase" 2>$null | Out-Null
    & git -C $root config --local --unset "branch.$branchName.pdxWorktree" 2>$null | Out-Null

    $head = (Get-GitText @('rev-parse', '--short', $base))
    Write-Host ''
    Write-Host "已合回 $base：$mergeMessage" -ForegroundColor Green
    Write-Host "SESSION_MERGED=$head"
    if ($mergeIn) { Write-Host "SESSION_BASE_OWNER=$mergeIn" }
    if ($selfLinked -or $pdxWorktree) {
        Write-Host ''
        Write-Host '这棵工作树已不再检出对话分支（分支已合回并删除）：' -ForegroundColor Yellow
        Write-Host "  当前状态：$(git -C $root rev-parse --short HEAD) detached"
        if ($pdxWorktree) { Write-Host "  清理：git worktree remove `"$pdxWorktree`"" }
        Write-Host '  主工作树不用清理，它仍在原分支上。'
    }
}

function Invoke-List {
    Assert-Git

    $integrationBranches = @(Get-IntegrationBranches)
    if ($integrationBranches.Count -gt 0) {
        Write-Host ("当前活跃版本分支：" + ($integrationBranches -join '  '))
    } else {
        Write-Warn '[提醒] integration-branch 里没有活跃版本分支。'
    }
    Write-Host '并行写法：open <slug> -Worktree（各占一棵树，互不遮挡）；合并串行，同一时刻只跑一个 close。'

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
        $wtPath = Get-BranchWorktree $branchName
        $worktrees = @(Get-WorktreesForBranch $branchName)
        foreach ($path in $worktrees) { $notes += "worktree=$path" }
        $checkPath = if ($wtPath) { $wtPath } elseif ($worktrees.Count -gt 0) { $worktrees[0] } else { $null }
        if ($checkPath) {
            $wtDirty = Get-DirtyCountAt $checkPath
            if ($wtDirty -eq $null) { $notes += '那棵树不可读' }
            elseif ($wtDirty -gt 0) { $notes += "脏 $wtDirty" }
            else { $notes += '干净' }
        }
        if ($branchName -eq $current) { $notes += '当前' }
        $noteText = ($notes -join ' ')
        Write-Host ("{0,-58} {1,-11} {2,4}  {3}" -f $branchName, $base, $ahead, $noteText)
    }

    # 基线被谁检出：被占用的基线会让「同时跑两个 close」变成排队，这里事先说清楚。
    $baseOwners = @{}
    foreach ($branchName in $refs) {
        $base = Get-BranchBase $branchName
        if (-not $base) { continue }
        if ($baseOwners.ContainsKey($base)) { continue }
        $owner = Get-WorktreeOwningBranch $base
        if ($owner) { $baseOwners[$base] = $owner }
    }
    if ($baseOwners.Count -gt 0) {
        Write-Host ''
        Write-Host '基线所在的工作树（close 会在这里合并）：'
        foreach ($key in $baseOwners.Keys) {
            Write-Host ("  {0} -> {1}" -f $key, $baseOwners[$key])
        }
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
