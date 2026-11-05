namespace PdxLauncher.Checks;

using System.Security.Cryptography;
using System.Text;

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
        "saves.action.restore",
        "saves.backup.created",
        "saves.backup.empty",
        "saves.backup.quotaFull",
        "saves.backup.rowName",
        "saves.backup.title",
        "saves.backup.usage",
        "saves.confirm.deleteBackup.content",
        "saves.confirm.deleteBackup.title",
        "saves.confirm.deleteSave.content",
        "saves.confirm.deleteSave.title",
        "saves.confirm.restore.content",
        "saves.confirm.restore.title",
        "saves.notice.backedUp",
        "saves.notice.deletedBackup",
        "saves.notice.deletedSave",
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
            var backupRow = ReadRequired(
                workspace,
                "备份行 ViewModel",
                "src",
                "PdxLauncher.App",
                "ViewModels",
                "SaveBackupRowViewModel.cs");

            if (page is null || xaml is null || viewModel is null || backupRow is null)
            {
                return ChecksContext.Failures - before;
            }

            CheckDestructiveFocus(page);
            CheckRestoreDefaults(viewModel);
            CheckQuotaGuard(viewModel);
            CheckFailureMessages(viewModel);
            CheckActionCommitGuard(viewModel);
            CheckBackupRow(backupRow);
            CheckXamlWiring(xaml);
            CheckNoDirectDelete(page, viewModel);
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

    private static void CheckDestructiveFocus(string page)
    {
        Console.WriteLine("[SavesActionsChecks] 破坏性确认框的初始焦点");

        var deleteSave = SliceMethod(page, "private async void OnDeleteSaveClick");
        var deleteBackup = SliceMethod(page, "private async void OnDeleteBackupClick");
        var restore = SliceMethod(page, "private async void OnRestoreBackupClick");

        ChecksContext.Check(
            "存档页: 删除存档的确认框默认焦点在「取消」",
            deleteSave.Contains("initialFocus: InitialFocus.Cancel", StringComparison.Ordinal),
            "真实存档删除是不可逆动作，Enter 不能默认触发删除");
        ChecksContext.Check(
            "存档页: 删除备份的确认框默认焦点在「取消」",
            deleteBackup.Contains("initialFocus: InitialFocus.Cancel", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 恢复的确认框保持默认「确认」焦点",
            !restore.Contains("InitialFocus.Cancel", StringComparison.Ordinal),
            "恢复默认 overwrite:false，只新建副本，不属于破坏性操作");

        ChecksContext.Check(
            "存档页: 删除存档走 DeleteSaveAsync",
            deleteSave.Contains("DeleteSaveAsync(row, _lifetime.Token)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 删除备份走 DeleteBackupAsync",
            deleteBackup.Contains("DeleteBackupAsync(row, _lifetime.Token)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 恢复走 RestoreAsync",
            restore.Contains("RestoreAsync(row, _lifetime.Token)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 构造时注入 AppServices.SaveBackups",
            page.Contains(
                "new SavesViewModel(AppServices.SaveGameFiles, AppServices.SaveBackups, AppServices.Logger)",
                StringComparison.Ordinal));
    }

    private static void CheckRestoreDefaults(string viewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 恢复默认不覆盖");

        var restore = SliceMethod(viewModel, "public async Task RestoreAsync");
        ChecksContext.Check(
            "视图模型: 恢复显式传 overwrite: false",
            restore.Contains("overwrite: false", StringComparison.Ordinal),
            "默认覆盖会毁掉玩家当前存档，必须是新建副本");
        ChecksContext.Check(
            "视图模型: 恢复成功后重扫存档列表（副本立刻可见）",
            restore.Contains("await LoadAsync(game, cancellationToken)", StringComparison.Ordinal));
    }

    private static void CheckQuotaGuard(string viewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 配额满禁止备份");

        var backup = SliceMethod(viewModel, "public async Task BackupAsync");
        var guard = backup.IndexOf("if (IsQuotaFull)", StringComparison.Ordinal);
        var call = backup.IndexOf(".BackupAsync(", StringComparison.Ordinal);

        ChecksContext.Check("视图模型: 备份前判断 IsQuotaFull", guard >= 0);
        ChecksContext.Check(
            "视图模型: 配额满的拦截发生在调用服务之前",
            guard >= 0 && call > guard,
            "配额满时不应再发起必然失败的 IO");
        ChecksContext.Check(
            "视图模型: 配额满提示「可删除旧备份」",
            backup.Contains("Keys.SavesBackupQuotaFull", StringComparison.Ordinal));
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
    private static void CheckActionCommitGuard(string viewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 动作完成回调带代次与游戏双重闸门");

        ChecksContext.Check(
            "视图模型: 写操作统一走 TryCommitAction",
            viewModel.Contains("TryCommitAction(int generation, GameInstallation game", StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 提交前比较当前游戏",
            viewModel.Contains("ReferenceEquals(Game, game)", StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 提交仍受扫描代次约束",
            viewModel.Contains("_requests.TryCommit(", StringComparison.Ordinal)
            && viewModel.Contains("generation, () =>", StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 备份列表读取绑定游戏实例",
            viewModel.Contains("LoadBackupsAsync(GameInstallation game", StringComparison.Ordinal));
        ChecksContext.Check(
            "视图模型: 旧游戏备份列表不能覆盖新列表",
            viewModel.Contains("version == _backupLoadVersion", StringComparison.Ordinal));

        var backup = SliceMethod(viewModel, "public async Task BackupAsync");
        var begin = backup.IndexOf("BeginAction()", StringComparison.Ordinal);
        var start = backup.IndexOf(".BackupAsync(", StringComparison.Ordinal);
        ChecksContext.Check(
            "视图模型: 动作开始即进入忙碌态（禁止并发写/切页刷新）",
            begin >= 0 && start > begin);
        ChecksContext.Check(
            "视图模型: 动作结束在 finally 清忙碌态",
            backup.Contains("EndAction()", StringComparison.Ordinal));
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
            "备份行: 恢复/删除按钮受 IsBusy 控制",
            backupRow.Contains("CanRestore => !IsBusy", StringComparison.Ordinal)
                && backupRow.Contains("CanDelete => !IsBusy", StringComparison.Ordinal));
    }

    private static void CheckXamlWiring(string xaml)
    {
        Console.WriteLine("[SavesActionsChecks] XAML 接线");

        foreach (var id in new[]
                 {
                     "btnBackupSave",
                     "btnDeleteSave",
                     "btnRestoreBackup",
                     "btnDeleteBackup",
                     "listSaveBackups",
                     "panelSaveBackups",
                     "infoSavesSuccess",
                     "infoSavesError",
                 })
        {
            ChecksContext.Check(
                "XAML: 存在 " + id + " 控件",
                xaml.Contains(@"AutomationId=""" + id + @"""", StringComparison.Ordinal),
                id);
        }

        ChecksContext.Check(
            "XAML: 备份按钮绑定 CanBackup",
            xaml.Contains(@"IsEnabled=""{x:Bind CanBackup, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "XAML: 删除存档按钮绑定 CanDelete",
            xaml.Contains(@"IsEnabled=""{x:Bind CanDelete, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "XAML: 备份段绑定 ViewModel.Backups",
            xaml.Contains(@"ItemsSource=""{x:Bind ViewModel.Backups, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "XAML: 空备份显示「还没有备份」",
            xaml.Contains("L.SavesBackupEmpty", StringComparison.Ordinal));
        ChecksContext.Check(
            "XAML: 未选游戏时隐藏整个备份段",
            xaml.Contains("XamlVisibility.FromBool(ViewModel.HasGame)", StringComparison.Ordinal));
        ChecksContext.Check(
            "XAML: 备份用量显示在标题旁",
            xaml.Contains("ViewModel.BackupUsageText", StringComparison.Ordinal));
        ChecksContext.Check(
            "XAML: 点击处理齐全",
            xaml.Contains(@"Click=""OnBackupSaveClick""", StringComparison.Ordinal)
                && xaml.Contains(@"Click=""OnDeleteSaveClick""", StringComparison.Ordinal)
                && xaml.Contains(@"Click=""OnRestoreBackupClick""", StringComparison.Ordinal)
                && xaml.Contains(@"Click=""OnDeleteBackupClick""", StringComparison.Ordinal));
        ChecksContext.Check(
            "XAML: 备份行有读屏名（避免整列读类型名）",
            xaml.Contains(@"AutomationProperties.Name=""{x:Bind AutomationName, Mode=OneWay}""", StringComparison.Ordinal));
    }

    private static void CheckNoDirectDelete(string page, string viewModel)
    {
        Console.WriteLine("[SavesActionsChecks] 不直接删文件");

        ChecksContext.Check(
            "存档页/视图模型: 没有 File.Delete（真实删除只经服务移入 trash）",
            !page.Contains("File.Delete", StringComparison.Ordinal)
                && !viewModel.Contains("File.Delete", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页/视图模型: 不写 sqlite 清单（磁盘为真源）",
            !page.Contains("sqlite", StringComparison.OrdinalIgnoreCase)
                && !viewModel.Contains("sqlite", StringComparison.OrdinalIgnoreCase));
    }

    private static void CheckLocalizationKeys(string workspace)
    {
        var zhPath = Path.Combine(workspace, "tools", "i18n", "zh-CN", "saves.json");
        var enPath = Path.Combine(workspace, "tools", "i18n", "en-US", "saves.json");
        if (!ChecksContext.Check("存档页文案: 中文源存在", File.Exists(zhPath), zhPath)
            || !ChecksContext.Check("存档页文案: 英文源存在", File.Exists(enPath), enPath))
        {
            return;
        }

        var zh = File.ReadAllText(zhPath);
        var en = File.ReadAllText(enPath);

        foreach (var key in NewLocalizationKeys)
        {
            var token = "\"" + key + "\"";
            ChecksContext.Check(
                "存档页文案: 中英都有 " + key,
                zh.Contains(token, StringComparison.Ordinal) && en.Contains(token, StringComparison.Ordinal));
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
