namespace PdxLauncher.Checks;

using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using PdxLauncher.App.Localization;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Saves;

/// <summary>
/// 存档页「备份 / 恢复 / 删除」接线的自检：一半跑真实 <see cref="SaveBackupService"/>（临时目录），
/// 一半把页面/视图模型的源码文本钉死。
/// <para>
/// 源码断言的理由与其余 UI 检查相同：页面与 ViewModel 引用了 WinUI，Checks 不能加载 App；
/// 但「删除前默认焦点在取消」「恢复默认 overwrite:false」「配额满不发起备份」这几条一旦被改回去，
/// 用户侧是不可逆的（真实存档被删 / 被覆盖 / 备份控件看起来可用却必然失败），必须能在自检里变红。
/// </para>
/// </summary>
public static class SavesActionsChecks
{
    private const string GameId = "ck3";

    /// <summary>本次新增的 i18n 键；中英两份源文件都必须有。</summary>
    private static readonly string[] NewLocalizationKeys =
    [
        "saves.action.backup",
        "saves.action.delete",
        "saves.action.deleteBackup",
        "saves.action.manageBackups",
        "saves.action.pruneBackups",
        "saves.action.restore",
        "saves.action.restoreOverwrite",
        "saves.backup.autoPruned",
        "saves.backup.created",
        "saves.backup.empty",
        "saves.backup.openFolder",
        "saves.backup.openFolderFailed",
        "saves.backup.orphan",
        "saves.backup.pageBack",
        "saves.backup.quotaFull",
        "saves.backup.quotaHint",
        "saves.backup.quotaLabel",
        "saves.backup.rowName",
        "saves.backup.title",
        "saves.backup.usage",
        "saves.confirm.deleteBackup.content",
        "saves.confirm.deleteBackup.title",
        "saves.confirm.deleteSave.content",
        "saves.confirm.deleteSave.title",
        "saves.confirm.pruneBackups.content",
        "saves.confirm.pruneBackups.title",
        "saves.confirm.restore.content",
        "saves.confirm.restore.title",
        "saves.confirm.restoreOverwrite.content",
        "saves.confirm.restoreOverwrite.title",
        "saves.notice.backedUp",
        "saves.notice.deletedBackup",
        "saves.notice.deletedSave",
        "saves.notice.pruned",
        "saves.notice.restored",
        "saves.notice.sourceMissing",
    ];

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var root = PlaysetChecks.CreateTempRoot(fs, "saves-actions");

        try
        {
            CheckServiceBehaviour(fs, logger, root);
            CheckQuotaAndGlobalBusy(fs, logger, root);
            CheckPruneBehaviour(fs, logger, root);
            CheckOrphanBehaviour(fs, logger, root);

            var workspace = FindWorkspaceRoot();
            var page = ReadRequired(
                workspace,
                "存档页代码",
                "src",
                "PdxLauncher.App",
                "Views",
                "SavesPage.xaml.cs");
            var xaml = ReadRequired(
                workspace,
                "存档页 XAML",
                "src",
                "PdxLauncher.App",
                "Views",
                "SavesPage.xaml");
            var viewModel = ReadRequired(
                workspace,
                "存档 ViewModel",
                "src",
                "PdxLauncher.App",
                "ViewModels",
                "SavesViewModel.cs");
            var backupPage = ReadRequired(
                workspace,
                "备份子页代码",
                "src",
                "PdxLauncher.App",
                "Views",
                "SaveBackupsPage.xaml.cs");
            var backupXaml = ReadRequired(
                workspace,
                "备份子页 XAML",
                "src",
                "PdxLauncher.App",
                "Views",
                "SaveBackupsPage.xaml");
            var backupViewModel = ReadRequired(
                workspace,
                "备份子页 ViewModel",
                "src",
                "PdxLauncher.App",
                "ViewModels",
                "SaveBackupsViewModel.cs");
            var backupRow = ReadRequired(
                workspace,
                "备份行 ViewModel",
                "src",
                "PdxLauncher.App",
                "ViewModels",
                "SaveBackupRowViewModel.cs");

            if (page is null || xaml is null || viewModel is null || backupRow is null
                || backupPage is null || backupXaml is null || backupViewModel is null)
            {
                return ChecksContext.Failures - before;
            }

            CheckDestructiveFocus(page, backupPage);
            CheckRestoreDefaults(backupPage, backupViewModel);
            CheckAutoPruneWiring(viewModel);
            CheckFailureMessages(viewModel);
            CheckActionCommitGuard(viewModel, backupViewModel);
            CheckBackupRow(backupRow);
            CheckPruneWiring(backupPage, backupViewModel);
            CheckXamlWiring(xaml, backupXaml);
            CheckNavigationWiring(workspace, page, backupPage);
            CheckNoDirectDelete(page, viewModel, backupPage, backupViewModel);
            CheckLocalizationKeys(workspace);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }

        return ChecksContext.Failures - before;
    }

    // ------------------------------------------------------------------
    // 真实服务行为（临时目录，绝不碰真机存档）
    // ------------------------------------------------------------------

    private static void CheckServiceBehaviour(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SavesActionsChecks] 备份 / 恢复 / 删除行为");

        var fixtureRoot = Path.Combine(root, "fixture");
        var dataRoot = Path.Combine(fixtureRoot, "data");
        var savesDirectory = Path.Combine(fixtureRoot, "saves");
        fs.EnsureDirectory(savesDirectory);

        var service = new SaveBackupService(fs, logger, dataRoot);
        var sourcePath = Path.Combine(savesDirectory, "测试 存档.ck3");
        var payload = Encoding.UTF8.GetBytes("payload 存档 🐟");
        fs.WriteAllBytesAtomic(sourcePath, payload);

        var backup = Await(service.BackupAsync(GameId, sourcePath, null, CancellationToken.None));
        ChecksContext.Equal("备份: 成功", SaveBackupStatus.Ok, backup.Status);
        ChecksContext.Check(
            "备份: 正文落在数据根 save-backups 下",
            backup.Entry is { } entry && entry.BackupPath.StartsWith(
                SaveBackupPaths.Root(dataRoot),
                StringComparison.OrdinalIgnoreCase),
            backup.Entry?.BackupPath);
        ChecksContext.Check(
            "备份: sidecar 与正文同目录且存在",
            backup.Entry is { } withSidecar && fs.FileExists(withSidecar.SidecarPath));

        var usage = Await(service.GetUsageAsync(CancellationToken.None));
        ChecksContext.Equal("备份: 用量条目 1", 1, usage.EntryCount);
        ChecksContext.Equal("备份: 用量字节等于源大小", (long)payload.Length, usage.TotalBytes);

        var listed = Await(service.ListBackupsAsync(GameId, CancellationToken.None));
        ChecksContext.Equal("备份: 列表返回 1 条", 1, listed.Count);

        var restore = Await(service.RestoreAsync(backup.Entry!, overwrite: false, CancellationToken.None));
        ChecksContext.Equal("恢复: 成功", SaveRestoreStatus.Ok, restore.Status);
        ChecksContext.Check(
            "恢复: 新文件名带「(恢复 yyyyMMdd-HHmm)」",
            restore.RestoredPath is { } restoredPath
                && Path.GetFileName(restoredPath).Contains(" (恢复 ", StringComparison.Ordinal),
            restore.RestoredPath);
        ChecksContext.Check("恢复: 原存档未被覆盖，仍然存在", fs.FileExists(sourcePath));
        ChecksContext.Check(
            "恢复: 副本与源内容一致",
            restore.RestoredPath is { } copyPath
                && Convert.ToHexString(SHA256.HashData(fs.ReadAllBytes(copyPath)))
                    == Convert.ToHexString(SHA256.HashData(payload)));

        var deleteSave = Await(service.DeleteSaveAsync(GameId, sourcePath, CancellationToken.None));
        ChecksContext.Equal("删除存档: 成功", SaveDeleteStatus.Ok, deleteSave.Status);
        ChecksContext.Check("删除存档: 原路径已不存在", !fs.FileExists(sourcePath));
        ChecksContext.Check(
            "删除存档: 文件在 trash 目录里（只移动，不永久删除）",
            deleteSave.TrashedPath is { } trashedPath
                && fs.FileExists(trashedPath)
                && trashedPath.Contains(SaveBackupPaths.TrashFolderName, StringComparison.OrdinalIgnoreCase),
            deleteSave.TrashedPath);

        var repeatDelete = Await(service.DeleteSaveAsync(GameId, sourcePath, CancellationToken.None));
        ChecksContext.Equal("删除存档: 已不存在返回 NotFound", SaveDeleteStatus.NotFound, repeatDelete.Status);
        ChecksContext.Check(
            "删除存档: NotFound 文案说明「不存在」",
            repeatDelete.Message.Contains("不存在", StringComparison.Ordinal),
            repeatDelete.Message);

        var missingSource = Await(service.BackupAsync(GameId, sourcePath, null, CancellationToken.None));
        ChecksContext.Equal("备份: 源缺失返回 SourceMissing", SaveBackupStatus.SourceMissing, missingSource.Status);

        var quotaDataRoot = Path.Combine(fixtureRoot, "quota-data");
        var quotaService = new SaveBackupService(fs, logger, quotaDataRoot, maxTotalBytes: 16);
        var quotaSource = Path.Combine(savesDirectory, "quota.ck3");
        fs.WriteAllBytesAtomic(quotaSource, new byte[64]);
        var quota = Await(quotaService.BackupAsync(GameId, quotaSource, null, CancellationToken.None));
        ChecksContext.Equal("配额: 超过总量上限返回 QuotaExceeded", SaveBackupStatus.QuotaExceeded, quota.Status);
        ChecksContext.Check(
            "配额: 文案说清是配额上限（不自动清理）",
            quota.Message.Contains("配额", StringComparison.Ordinal),
            quota.Message);
    }

    // ------------------------------------------------------------------
    // 页面 / 视图模型源码断言
    // ------------------------------------------------------------------

    /// <summary>
    /// 配额满原因必须真的出现在可见文案里；全局忙碌时必须把删除/恢复也禁用，
    /// 不能只在 handler 里 return（用户点了确认后静默 no-op）。
    /// </summary>
    private static void CheckQuotaAndGlobalBusy(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SavesActionsChecks] 配额满提示与全局忙碌禁用");

        var scanner = new SaveGameScanner(
            fs,
            logger,
            new SaveLocationCatalog(
                fs,
                Path.Combine(root, "vm-docs"),
                Path.Combine(root, "vm-local"),
                Path.Combine(root, "vm-low")));
        var backups = new SaveBackupService(fs, logger, Path.Combine(root, "vm-data"));
        var viewModel = new SavesViewModel(scanner, backups, logger);

        var usageField = typeof(SavesViewModel).GetField(
            "_usage",
            BindingFlags.Instance | BindingFlags.NonPublic);
        ChecksContext.Check("视图模型: 可注入备份用量做配额自检", usageField is not null, "缺少 _usage 字段");
        if (usageField is not null)
        {
            usageField.SetValue(
                viewModel,
                new SaveBackupUsage(SaveBackupPaths.DefaultMaxTotalBytes, 0));
            ChecksContext.Check(
                "视图模型: 用量行自带上限（不再需要独立提示块）",
                viewModel.BackupUsageText.Contains(
                    SizeFormatter.Format(SaveBackupPaths.DefaultMaxTotalBytes),
                    StringComparison.Ordinal),
                viewModel.BackupUsageText);
            ChecksContext.Check(
                "视图模型: 配额满不再把「空间已满」拼进存档页摘要",
                !viewModel.BackupUsageText.Contains(Keys.SavesBackupQuotaFull, StringComparison.Ordinal),
                "超量由服务自动清理，提示改在备份子页显示");

            usageField.SetValue(viewModel, new SaveBackupUsage(0, 0));
            ChecksContext.Check(
                "视图模型: 用量归零后摘要正常",
                viewModel.BackupUsageText.Contains(SizeFormatter.Format(0), StringComparison.Ordinal),
                viewModel.BackupUsageText);
        }

        var file = new SaveFileEntry(
            GameId,
            Path.Combine(root, "a.ck3"),
            "a.ck3",
            1,
            DateTimeOffset.UtcNow,
            SaveContainer.TextMeta);
        var gameRow = new SaveGameRowViewModel(file, 0);
        ChecksContext.Check("行按钮: 全局空闲时允许删除", gameRow.CanDelete);
        gameRow.SetActionAllowed(false);
        ChecksContext.Check("行按钮: 全局忙碌时删除禁用", !gameRow.CanDelete);

        var backupEntry = new SaveBackupEntry(
            Path.Combine(root, "a.ck3.bak"),
            Path.Combine(root, "a.ck3.bak.json"),
            file.FilePath,
            "a.ck3",
            1,
            "sha256",
            GameId,
            null,
            null,
            DateTimeOffset.UtcNow);
        var backupRow = new SaveBackupRowViewModel(backupEntry, 0);
        ChecksContext.Check("备份行: 全局空闲时允许恢复", backupRow.CanRestore);
        backupRow.SetActionAllowed(false);
        ChecksContext.Check("备份行: 全局忙碌时恢复禁用", !backupRow.CanRestore);
        ChecksContext.Check("备份行: 全局忙碌时删除备份禁用", !backupRow.CanDelete);
    }

    /// <summary>
    /// 清理旧备份是真的删除：保留最新 N 份，其余正文与 sidecar 一起删掉；
    /// 这里直接跑服务，确认返回份数、磁盘剩余与用量三者一致。
    /// </summary>
    private static void CheckPruneBehaviour(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SavesActionsChecks] 清理旧备份行为");

        var fixtureRoot = Path.Combine(root, "prune");
        var dataRoot = Path.Combine(fixtureRoot, "data");
        var savesDirectory = Path.Combine(fixtureRoot, "saves");
        fs.EnsureDirectory(savesDirectory);

        var service = new SaveBackupService(fs, logger, dataRoot);
        for (var index = 0; index < 4; index++)
        {
            var source = Path.Combine(savesDirectory, "prune-" + index + ".ck3");
            fs.WriteAllBytesAtomic(source, Encoding.UTF8.GetBytes("payload " + index));
            var backedUp = Await(service.BackupAsync(GameId, source, null, CancellationToken.None));
            ChecksContext.Equal("清理: 准备第 " + (index + 1) + " 份备份", SaveBackupStatus.Ok, backedUp.Status);
        }

        var before = Await(service.GetUsageAsync(CancellationToken.None));
        ChecksContext.Equal("清理: 清理前有 4 份", 4, before.EntryCount);

        var pruned = Await(service.PruneAsync(GameId, keepNewest: 2, CancellationToken.None));
        ChecksContext.Equal("清理: 显式清理成功", SaveBackupStatus.Ok, pruned.Status);
        ChecksContext.Equal("清理: 删掉 2 份", 2, pruned.DeletedEntries);
        ChecksContext.Equal("清理: 没有失败删除", 0, pruned.FailedDeletions);
        ChecksContext.Check(
            "清理: 文案报告已清理份数",
            pruned.Message.Contains("已清理 2", StringComparison.Ordinal),
            pruned.Message);

        var remaining = Await(service.ListBackupsAsync(GameId, CancellationToken.None));
        ChecksContext.Equal("清理: 磁盘只剩最新 2 份", 2, remaining.Count);
        ChecksContext.Check(
            "清理: 剩余条目仍可读 sidecar",
            remaining.All(entry => !entry.IsDegraded && fs.FileExists(entry.SidecarPath)));

        var after = Await(service.GetUsageAsync(CancellationToken.None));
        ChecksContext.Equal("清理: 用量条目同步为 2", 2, after.EntryCount);
    }

    /// <summary>
    /// sidecar 缺失的 .bak 以前只占配额、不出现在列表里，UI 永远删不掉；
    /// 现在必须是「降级条目」：可见、带真实大小、不能恢复但可以删除。
    /// </summary>
    private static void CheckOrphanBehaviour(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SavesActionsChecks] 孤儿备份可见且可删");

        var dataRoot = Path.Combine(root, "orphan", "data");
        var service = new SaveBackupService(fs, logger, dataRoot);
        var gameDirectory = SaveBackupPaths.GameDirectory(dataRoot, GameId);
        fs.EnsureDirectory(gameDirectory);

        var orphanPath = Path.Combine(gameDirectory, "孤儿 存档.ck3.bak");
        fs.WriteAllBytesAtomic(orphanPath, new byte[123]);

        var usage = Await(service.GetUsageAsync(CancellationToken.None));
        ChecksContext.Equal("孤儿: 配额统计包含它", 1, usage.EntryCount);

        var listed = Await(service.ListBackupsAsync(GameId, CancellationToken.None));
        if (!ChecksContext.Equal("孤儿: 列表里能看见（不再只占配额）", 1, listed.Count))
        {
            return;
        }

        var orphan = listed[0];
        ChecksContext.Check("孤儿: 标记为降级条目", orphan.IsDegraded);
        ChecksContext.Equal("孤儿: 保留磁盘文件名", "孤儿 存档.ck3.bak", orphan.OriginalFileName);
        ChecksContext.Equal("孤儿: 大小取磁盘实际值", 123L, orphan.SizeBytes);
        ChecksContext.Check("孤儿: 没有可恢复的哈希", orphan.Sha256.Length == 0);

        var row = new SaveBackupRowViewModel(orphan, 0);
        ChecksContext.Check(
            "孤儿行: 显示「缺少说明文件」",
            row.IsDegraded && row.CreatedText == Keys.SavesBackupOrphan,
            row.CreatedText);
        ChecksContext.Check("孤儿行: 禁止恢复（没有哈希）", !row.CanRestore);
        ChecksContext.Check("孤儿行: 允许删除", row.CanDelete);

        var deleted = Await(service.DeleteBackupAsync(orphan, CancellationToken.None));
        ChecksContext.Equal("孤儿: 可以删除", SaveDeleteStatus.Ok, deleted.Status);
        ChecksContext.Check("孤儿: 删除后正文消失", !fs.FileExists(orphanPath));
    }

    private static void CheckDestructiveFocus(string page, string backupPage)
    {
        Console.WriteLine("[SavesActionsChecks] 破坏性确认框的初始焦点");

        var deleteSave = SliceMethod(page, "private async void OnDeleteSaveClick");
        var deleteBackup = SliceMethod(backupPage, "private async void OnDeleteBackupClick");
        var prune = SliceMethod(backupPage, "private async void OnPruneBackupsClick");
        var restore = SliceMethod(backupPage, "private async void OnRestoreBackupClick");
        var overwrite = SliceMethod(backupPage, "private async void OnRestoreOverwriteClick");

        ChecksContext.Check(
            "存档页: 删除存档的确认框默认焦点在「取消」",
            deleteSave.Contains("initialFocus: InitialFocus.Cancel", StringComparison.Ordinal),
            "真实存档删除是不可逆动作，Enter 不能默认触发删除");
        ChecksContext.Check(
            "备份页: 删除备份的确认框默认焦点在「取消」",
            deleteBackup.Contains("initialFocus: InitialFocus.Cancel", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页: 清理旧备份的确认框默认焦点在「取消」",
            prune.Contains("initialFocus: InitialFocus.Cancel", StringComparison.Ordinal),
            "清理会批量删除备份，Enter 不能默认触发");
        ChecksContext.Check(
            "备份页: 清理确认框文案带保留份数",
            prune.Contains("Keys.SavesConfirmPruneBackupsContent, SaveBackupsViewModel.PruneKeepNewest", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页: 确认后调用 ViewModel.PruneAsync",
            prune.Contains("_viewModel.PruneAsync(_lifetime.Token)", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页: 普通恢复默认 overwrite:false 且焦点保持「确认」",
            restore.Contains("RestoreAsync(row, overwrite: false", StringComparison.Ordinal)
                && !restore.Contains("InitialFocus.Cancel", StringComparison.Ordinal),
            "恢复默认只新建副本，不属于破坏性操作");
        ChecksContext.Check(
            "备份页: 覆盖恢复默认焦点在「取消」且显式 overwrite:true",
            overwrite.Contains("RestoreAsync(row, overwrite: true", StringComparison.Ordinal)
                && overwrite.Contains("initialFocus: InitialFocus.Cancel", StringComparison.Ordinal));

        ChecksContext.Check(
            "存档页: 删除存档走 DeleteSaveAsync",
            deleteSave.Contains("DeleteSaveAsync(row, _lifetime.Token)", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页: 删除备份走 DeleteBackupAsync",
            deleteBackup.Contains("DeleteBackupAsync(row, _lifetime.Token)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 构造时注入 AppServices.SaveBackups",
            page.Contains(
                "new SavesViewModel(AppServices.SaveGameFiles, AppServices.SaveBackups, AppServices.Logger)",
                StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页: 构造时注入 AppServices.SaveBackups",
            backupPage.Contains(
                "new SaveBackupsViewModel(AppServices.SaveBackups, AppServices.Logger)",
                StringComparison.Ordinal));
    }

    private static void CheckRestoreDefaults(string backupPage, string backupViewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 恢复默认不覆盖");

        var restore = SliceMethod(backupViewModel, "public async Task RestoreAsync");
        ChecksContext.Check(
            "备份页视图模型: 恢复把 overwrite 原样交给服务",
            restore.Contains(".RestoreAsync(row.Entry, overwrite, cancellationToken)", StringComparison.Ordinal),
            "页面负责区分「恢复」与「覆盖恢复」，视图模型不再写死 false");
        ChecksContext.Check(
            "备份页: 普通恢复显式传 overwrite:false",
            backupPage.Contains("RestoreAsync(row, overwrite: false", StringComparison.Ordinal),
            "默认覆盖会毁掉玩家当前存档，必须是新建副本");
        ChecksContext.Check(
            "备份页: 覆盖恢复显式传 overwrite:true",
            backupPage.Contains("RestoreAsync(row, overwrite: true", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页视图模型: 恢复成功提示带新文件名",
            restore.Contains("Keys.SavesNoticeRestored", StringComparison.Ordinal));
    }

    private static void CheckAutoPruneWiring(string viewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 超量自动清理接线");

        var backup = SliceMethod(viewModel, "public async Task BackupAsync");
        ChecksContext.Check(
            "视图模型: 不再在调用服务前用 IsQuotaFull 拦截",
            !backup.Contains("if (IsQuotaFull)", StringComparison.Ordinal),
            "自动清理需要服务真正收到备份请求，页面预拦截会让它永远触发不到");
        ChecksContext.Check(
            "视图模型: 成功提示报告自动清理份数",
            backup.Contains("result.AutoPrunedEntries > 0", StringComparison.Ordinal)
                && backup.Contains("Keys.SavesBackupAutoPruned", StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 全局忙碌时统一下发 SetActionAllowed",
            viewModel.Contains("row.SetActionAllowed(actionAllowed)", StringComparison.Ordinal)
                && viewModel.Contains("var actionAllowed = !IsBusy;", StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 备份按钮只受忙碌态约束",
            viewModel.Contains("row.SetBackupAllowed(actionAllowed)", StringComparison.Ordinal)
                && !viewModel.Contains("SetBackupAllowed(backupAllowed)", StringComparison.Ordinal));
    }

    private static void CheckFailureMessages(string viewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 失败文案映射");

        ChecksContext.Check(
            "视图模型: 备份 QuotaExceeded 映射到配额提示",
            viewModel.Contains(
                "SaveBackupStatus.QuotaExceeded => T(Keys.SavesBackupQuotaFull)",
                StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 备份 SourceMissing 映射到「已刷新」提示",
            viewModel.Contains(
                "SaveBackupStatus.SourceMissing => T(Keys.SavesNoticeSourceMissing)",
                StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 删除 NotFound 映射到「已刷新」提示",
            viewModel.Contains(
                "SaveDeleteStatus.NotFound => T(Keys.SavesNoticeSourceMissing)",
                StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 其它失败直接显示服务返回的 Message",
            viewModel.Contains("_ => result.Message", StringComparison.Ordinal));
    }

    /// <summary>
    /// 写操作跨越 await 后必须同时检查扫描代次和当前游戏；否则用户切游戏后，
    /// 旧动作的完成回调会把旧存档/备份提示写进新页面。这里把三道闸门钉在源码里。
    /// </summary>
    private static void CheckActionCommitGuard(string viewModel, string backupViewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 动作完成回调带代次与游戏双重闸门");

        ChecksContext.Check(
            "存档视图模型: 写操作统一走 TryCommitAction",
            viewModel.Contains("TryCommitAction(int generation, GameInstallation game", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档视图模型: 提交前比较当前游戏",
            viewModel.Contains("ReferenceEquals(Game, game)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档视图模型: 提交仍受扫描代次约束",
            viewModel.Contains("_requests.TryCommit(", StringComparison.Ordinal)
            && viewModel.Contains("generation, () =>", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档视图模型: 备份列表读取绑定游戏实例",
            viewModel.Contains("LoadBackupsAsync(GameInstallation game", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档视图模型: 旧游戏备份列表不能覆盖新列表",
            viewModel.Contains("version == _backupLoadVersion", StringComparison.Ordinal));

        var backup = SliceMethod(viewModel, "public async Task BackupAsync");
        var begin = backup.IndexOf("BeginAction()", StringComparison.Ordinal);
        var start = backup.IndexOf(".BackupAsync(", StringComparison.Ordinal);
        ChecksContext.Check(
            "存档视图模型: 动作开始即进入忙碌态（禁止并发写/切页刷新）",
            begin >= 0 && start > begin);
        ChecksContext.Check(
            "存档视图模型: 动作结束在 finally 清忙碌态",
            backup.Contains("EndAction()", StringComparison.Ordinal));

        ChecksContext.Check(
            "备份视图模型: 写操作统一走 TryCommitAction",
            backupViewModel.Contains("TryCommitAction(int generation, GameInstallation game", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份视图模型: 提交前比较当前游戏",
            backupViewModel.Contains("ReferenceEquals(Game, game)", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份视图模型: 读取受代次守卫约束",
            backupViewModel.Contains("_requests.TryCommit(", StringComparison.Ordinal)
                && backupViewModel.Contains("var generation = _requests.Begin();", StringComparison.Ordinal));
    }

    private static void CheckBackupRow(string backupRow)
    {
        Console.WriteLine("[SavesActionsChecks] 备份行");

        ChecksContext.Check(
            "备份行: 时间用「备份于」键而不是写死格式",
            backupRow.Contains("T(\n            Keys.SavesBackupCreated", StringComparison.Ordinal)
                || backupRow.Contains("Keys.SavesBackupCreated", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份行: 读屏名包含创建时间",
            backupRow.Contains("T(Keys.SavesBackupRowName, FileName, CreatedText)", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份行: 恢复/删除按钮受行忙碌与全局忙碌控制",
            backupRow.Contains("CanRestore => !IsDegraded && !IsBusy && _actionAllowed", StringComparison.Ordinal)
                && backupRow.Contains("CanDelete => !IsBusy && _actionAllowed", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份行: 孤儿备份显示「缺少说明文件」而不是伪造时间",
            backupRow.Contains("? T(Keys.SavesBackupOrphan)", StringComparison.Ordinal),
            "降级条目没有可信的 created 时间，不能冒充「备份于」");
        ChecksContext.Check(
            "备份行: 把条目的降级状态透出给 XAML",
            backupRow.Contains("public bool IsDegraded => Entry.IsDegraded;", StringComparison.Ordinal));
    }

    /// <summary>
    /// 清理入口的三段接线：可点（CanPrune）→ 二次确认（默认取消）→ 调服务 → 刷新列表。
    /// 「只删了 N 份提示却没刷新」会让用户仍看到已删除的行，所以刷新也必须钉住。
    /// </summary>
    private static void CheckPruneWiring(string backupPage, string backupViewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 清理旧备份接线");

        var prune = SliceMethod(backupPage, "private async void OnPruneBackupsClick");
        var pruneAsync = SliceMethod(backupViewModel, "public async Task PruneAsync");
        ChecksContext.Check(
            "备份视图模型: 清理保留份数集中在 PruneKeepNewest 常量",
            backupViewModel.Contains("public const int PruneKeepNewest = 10;", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份视图模型: 只在有游戏且有备份时允许清理",
            backupViewModel.Contains("CanPrune => HasGame && !IsBusy && HasBackups", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份视图模型: 清理调用服务 PruneAsync（带保留份数）",
            pruneAsync.Contains("_backups", StringComparison.Ordinal)
                && pruneAsync.Contains(".PruneAsync(game.GameId, PruneKeepNewest, cancellationToken)", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份视图模型: 成功提示报告已清理份数",
            pruneAsync.Contains("T(Keys.SavesNoticePruned, result.DeletedEntries)", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份视图模型: 失败时显示服务返回的原因",
            pruneAsync.Contains("SetNotice(result.Message, isError: true)", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份视图模型: 清理后刷新备份列表",
            pruneAsync.Contains("await LoadAsync(game, cancellationToken)", StringComparison.Ordinal),
            "部分失败也会改动磁盘，必须重读列表");
        ChecksContext.Check(
            "备份视图模型: 清理结果受游戏与代次双重闸门约束",
            pruneAsync.Contains("TryCommitAction(", StringComparison.Ordinal)
                && pruneAsync.Contains("generation,", StringComparison.Ordinal)
                && pruneAsync.Contains("game,", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页: 清理按钮在确认后才调用视图模型",
            prune.Contains("ConfirmOverlay.ConfirmAsync", StringComparison.Ordinal)
                && prune.Contains("_viewModel.PruneAsync(_lifetime.Token)", StringComparison.Ordinal));
    }

    private static void CheckXamlWiring(string xaml, string backupXaml)
    {
        Console.WriteLine("[SavesActionsChecks] XAML 接线");

        foreach (var id in new[]
                 {
                     "btnBackupSave",
                     "btnDeleteSave",
                     "panelSaveBackups",
                     "btnManageBackups",
                     "infoSavesSuccess",
                     "infoSavesError",
                 })
        {
            ChecksContext.Check(
                "存档页 XAML: 存在 " + id + " 控件",
                xaml.Contains(@"AutomationId=""" + id + @"""", StringComparison.Ordinal),
                id);
        }

        ChecksContext.Check(
            "存档页 XAML: 备份按钮绑定 CanBackup",
            xaml.Contains(@"IsEnabled=""{x:Bind CanBackup, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页 XAML: 删除存档按钮绑定 CanDelete",
            xaml.Contains(@"IsEnabled=""{x:Bind CanDelete, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页 XAML: 备份摘要只显示用量（唯一一处）",
            xaml.Split("ViewModel.BackupUsageText", StringSplitOptions.None).Length - 1 == 1);
        ChecksContext.Check(
            "存档页 XAML: 空备份显示「还没有备份」",
            xaml.Contains("L.SavesBackupEmpty", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页 XAML: 未选游戏时隐藏备份摘要",
            xaml.Contains("XamlVisibility.FromBool(ViewModel.HasGame)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页 XAML: 备份列表与恢复/清理控件已迁到子页",
            !xaml.Contains("listSaveBackups", StringComparison.Ordinal)
                && !xaml.Contains("btnRestoreBackup", StringComparison.Ordinal)
                && !xaml.Contains("btnPruneBackups", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页 XAML: 入口与行按钮的点击处理齐全",
            xaml.Contains(@"Click=""OnBackupSaveClick""", StringComparison.Ordinal)
                && xaml.Contains(@"Click=""OnDeleteSaveClick""", StringComparison.Ordinal)
                && xaml.Contains(@"Click=""OnManageBackupsClick""", StringComparison.Ordinal));

        foreach (var id in new[]
                 {
                     "btnSaveBackupsBack",
                     "btnRestoreBackup",
                     "btnRestoreOverwriteBackup",
                     "btnDeleteBackup",
                     "btnPruneBackups",
                     "btnOpenBackupFolder",
                     "btnBackupsRefresh",
                     "listSaveBackups",
                     "panelSaveBackups",
                     "numBackupQuota",
                     "infoBackupsSuccess",
                     "infoBackupsError",
                 })
        {
            ChecksContext.Check(
                "备份页 XAML: 存在 " + id + " 控件",
                backupXaml.Contains(@"AutomationId=""" + id + @"""", StringComparison.Ordinal),
                id);
        }

        ChecksContext.Check(
            "备份页 XAML: 列表绑定 ViewModel.Backups",
            backupXaml.Contains(@"ItemsSource=""{x:Bind ViewModel.Backups, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页 XAML: 行按钮分别绑定 CanRestore / CanDelete",
            backupXaml.Contains(@"IsEnabled=""{x:Bind CanRestore, Mode=OneWay}""", StringComparison.Ordinal)
                && backupXaml.Contains(@"IsEnabled=""{x:Bind CanDelete, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页 XAML: 清理按钮绑定 CanPrune 与文案键",
            backupXaml.Contains(@"IsEnabled=""{x:Bind ViewModel.CanPrune, Mode=OneWay}""", StringComparison.Ordinal)
                && backupXaml.Contains(@"Content=""{x:Bind L.SavesActionPruneBackups, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页 XAML: 配额输入与自动清理提示走 i18n",
            backupXaml.Contains("L.SavesBackupQuotaLabel", StringComparison.Ordinal)
                && backupXaml.Contains("L.SavesBackupQuotaHint", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页 XAML: 恢复/覆盖/删除/清理/返回/打开目录的点击处理齐全",
            backupXaml.Contains(@"Click=""OnRestoreBackupClick""", StringComparison.Ordinal)
                && backupXaml.Contains(@"Click=""OnRestoreOverwriteClick""", StringComparison.Ordinal)
                && backupXaml.Contains(@"Click=""OnDeleteBackupClick""", StringComparison.Ordinal)
                && backupXaml.Contains(@"Click=""OnPruneBackupsClick""", StringComparison.Ordinal)
                && backupXaml.Contains(@"Click=""OnBackClick""", StringComparison.Ordinal)
                && backupXaml.Contains(@"Click=""OnOpenBackupFolderClick""", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页 XAML: 备份行有读屏名（避免整列读类型名）",
            backupXaml.Contains(@"AutomationProperties.Name=""{x:Bind AutomationName, Mode=OneWay}""", StringComparison.Ordinal));
    }

    /// <summary>
    /// 子页导航四处接线：Tag 常量 → 类型映射 → 发起页事件 → MainWindow 订阅/执行。
    /// 少了任何一处，点「管理备份」都会静默无反应。
    /// </summary>
    private static void CheckNavigationWiring(string workspace, string page, string backupPage)
    {
        Console.WriteLine("[SavesActionsChecks] 备份子页导航接线");

        var navigation = ReadRequired(
            workspace,
            "导航服务",
            "src",
            "PdxLauncher.App",
            "Services",
            "NavigationService.cs");
        var mainWindow = ReadRequired(
            workspace,
            "主窗口",
            "src",
            "PdxLauncher.App",
            "MainWindow.xaml.cs");

        ChecksContext.Check(
            "导航: 存在 SaveBackupsTag 常量",
            navigation is not null && navigation.Contains("SaveBackupsTag = \"savebackups\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航: Tag 映射到 SaveBackupsPage",
            navigation is not null
                && navigation.Contains(
                    "[SaveBackupsTag] = \"PdxLauncher.App.Views.SaveBackupsPage\"",
                    StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 声明 BackupsRequested 事件",
            page.Contains("event EventHandler<SaveBackupsNavigationRequest>? BackupsRequested", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 管理备份入口构造请求并带上返回 Tag",
            page.Contains("new SaveBackupsNavigationRequest(game, NavigationService.SavesTag)", StringComparison.Ordinal));
        ChecksContext.Check(
            "主窗口: 订阅存档页的备份请求",
            mainWindow is not null
                && mainWindow.Contains("savesPage.BackupsRequested += OnSaveBackupsRequested;", StringComparison.Ordinal));
        ChecksContext.Check(
            "主窗口: 执行导航到 SaveBackupsTag",
            mainWindow is not null
                && mainWindow.Contains("_navigation.Navigate(NavigationService.SaveBackupsTag, request)", StringComparison.Ordinal));
        ChecksContext.Check(
            "主窗口: 离开页面时退订，避免持有已丢弃页面",
            mainWindow is not null
                && mainWindow.Contains("_savesPage.BackupsRequested -= OnSaveBackupsRequested;", StringComparison.Ordinal)
                && mainWindow.Contains("_saveBackupsPage.NavigationRequested -= OnPageNavigationRequested;", StringComparison.Ordinal));
        ChecksContext.Check(
            "备份页: 返回按钮回到请求里的 ReturnTag（默认存档页）",
            backupPage.Contains(
                "NavigationRequested?.Invoke(this, _request?.ReturnTag ?? NavigationService.SavesTag)",
                StringComparison.Ordinal));
    }

    private static void CheckNoDirectDelete(string page, string viewModel, string backupPage, string backupViewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 不直接删文件");

        ChecksContext.Check(
            "存档页/备份页/视图模型: 没有 File.Delete（真实删除只经服务移入 trash）",
            !page.Contains("File.Delete", StringComparison.Ordinal)
                && !viewModel.Contains("File.Delete", StringComparison.Ordinal)
                && !backupPage.Contains("File.Delete", StringComparison.Ordinal)
                && !backupViewModel.Contains("File.Delete", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页/备份页/视图模型: 不写 sqlite 清单（磁盘为真源）",
            !page.Contains("sqlite", StringComparison.OrdinalIgnoreCase)
                && !viewModel.Contains("sqlite", StringComparison.OrdinalIgnoreCase)
                && !backupPage.Contains("sqlite", StringComparison.OrdinalIgnoreCase)
                && !backupViewModel.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
    }

    private static void CheckLocalizationKeys(string workspace)
    {
        var zhPath = I18nResw.ResourcesPath(workspace, "zh-CN");
        var enPath = I18nResw.ResourcesPath(workspace, "en-US");
        if (!ChecksContext.Check("存档页文案: 中文源存在", File.Exists(zhPath), zhPath)
            || !ChecksContext.Check("存档页文案: 英文源存在", File.Exists(enPath), enPath))
        {
            return;
        }

        var zh = I18nResw.Read(workspace, "zh-CN");
        var en = I18nResw.Read(workspace, "en-US");

        foreach (var key in NewLocalizationKeys)
        {
            ChecksContext.Check(
                "存档页文案: 中英都有 " + key,
                I18nResw.Get(zh, key) is not null && I18nResw.Get(en, key) is not null);
        }
    }

    // ------------------------------------------------------------------
    // 工具
    // ------------------------------------------------------------------

    /// <summary>截取一个成员，遇到下一个「private/public」成员声明即停，避免误读到邻居的代码。</summary>
    private static string SliceMethod(string source, string anchor)
    {
        var index = source.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
        {
            return string.Empty;
        }

        var end = source.IndexOf("\n    private", index + anchor.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            end = source.IndexOf("\n    public", index + anchor.Length, StringComparison.Ordinal);
        }

        var stop = end < 0 ? source.Length : end;

        // 行尾的「public async」也是方法体内部可能出现的关键字；只在成员缩进处截断。
        return source.Substring(index, stop - index);
    }

    private static string Slice(string source, string anchor, int length)
    {
        var index = source.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
        {
            return string.Empty;
        }

        return source.Substring(index, Math.Min(length, source.Length - index));
    }

    private static string? ReadRequired(string root, string label, params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        return ChecksContext.Check("存档页: " + label + " 存在", File.Exists(path), path)
            ? File.ReadAllText(path)
            : null;
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
