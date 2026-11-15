namespace PdxLauncher.Checks;

using PdxLauncher.App.Localization;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 设置页「本地离线模型」自检：把这次修好的几处口径钉成源码断言。
/// <para>
/// 这一块之前翻过车：界面用单个 <c>IsInstalled</c> 布尔拼「已安装 / 未安装」，
/// 缺 <c>config.txt</c> 时显示成已安装、引擎却说「尚未下载」；删除同步跑在 UI 线程，
/// 删完立刻变未安装，用户不知道文件到底有没有删掉，也没有入口自己去看。
/// 断言读源文本而不是截图，理由同 <see cref="AccountPageLayoutChecks"/>。
/// </para>
/// </summary>
public static class LocalModelUiChecks
{
    /// <summary>新增的 i18n 键；中英两份源文件都必须有。</summary>
    private static readonly string[] NewLocalizationKeys =
    [
        "settings.translate.localModelOpenFolder",
        "settings.translate.localModelVerify",
        "settings.translate.localModelDialogRemoveTitle",
        "settings.translate.localModelDialogRemoveContent",
        "settings.translate.localModelStatusMissingFiles",
        "settings.translate.localModelStatusSizeMismatch",
        "settings.translate.localModelStatusConfigMissing",
        "settings.translate.localModelStatusVerifying",
        "settings.translate.localModelStatusVerifyPassed",
        "settings.translate.localModelStatusVerifyFailed",
        "settings.translate.localModelStatusVerifyMissing",
        "settings.translate.localModelStatusVerifySize",
        "settings.translate.localModelStatusVerifyConfigMissing",
        "settings.translate.localModelStatusVerifyHashMismatch",
        "settings.translate.localModelStatusRemovedVerified",
        "settings.translate.localModelStatusRemovedWithResidue",
        "settings.translate.localModelStatusResidueDirectoryOnly",
        "settings.translate.localModelStatusRemoveNotFound",
        "settings.translate.localModelRemoveFailed",
        "settings.translate.localModelResidueMore",
        "settings.translate.localModelRemoveTranslationBusy",
        "settings.translate.localModelStatusResidueReason",
        "settings.translate.localModelDownloadProgress",
        "settings.translate.localModelPaused",
        "settings.translate.localModelPause",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();

        var xaml = ReadRequired(root, "设置页 XAML", "src", "PdxLauncher.App", "Views", "SettingsPage.xaml");
        var pageCode = ReadRequired(root, "设置页代码", "src", "PdxLauncher.App", "Views", "SettingsPage.xaml.cs");
        var rowViewModel = ReadRequired(
            root,
            "模型行 ViewModel",
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "LocalModelRowViewModel.cs");
        var settingsViewModel = ReadRequired(
            root,
            "设置 ViewModel",
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "SettingsViewModel.cs");
        var bergamot = ReadRequired(
            root,
            "Bergamot 引擎",
            "src",
            "PdxLauncher.Core",
            "Online",
            "Translation",
            "Local",
            "BergamotTranslationEngine.cs");
        var indexTranslate = ReadRequired(
            root,
            "Index-Translate 引擎",
            "src",
            "PdxLauncher.Core",
            "Online",
            "Translation",
            "Local",
            "IndexTranslateEngine.cs");

        if (xaml is null || pageCode is null || rowViewModel is null || settingsViewModel is null
            || bergamot is null || indexTranslate is null)
        {
            return ChecksContext.Failures - before;
        }

        CheckRowButtons(xaml, pageCode);
        CheckFiveStateStatus(rowViewModel);
        CheckRemoveAndVerifyAreAsync(settingsViewModel, pageCode);
        CheckRemoveMessagesAreSelfProving(settingsViewModel);
        CheckOperationResultExpires(settingsViewModel, rowViewModel);
        CheckEnginesUseRunnable(bergamot, indexTranslate);
        CheckDownloadProgressUi(xaml, pageCode, settingsViewModel);
        CheckRowDownloadState();
        CheckLocalizationKeys(root);

        return ChecksContext.Failures - before;
    }

    /// <summary>每行必须有「打开文件夹」「校验」两个按钮，并且各自有可用性绑定与点击处理。</summary>
    private static void CheckRowButtons(string xaml, string pageCode)
    {
        foreach (var id in new[] { "btnOpenLocalModelFolder", "btnVerifyLocalModel" })
        {
            ChecksContext.Check(
                "本地模型: 行内有 " + id + " 按钮",
                xaml.Contains(@"AutomationId=""" + id + @"""", StringComparison.Ordinal),
                id);
        }

        ChecksContext.Check(
            "本地模型: 打开文件夹绑定 CanOpenFolder",
            xaml.Contains(@"IsEnabled=""{x:Bind CanOpenFolder, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 校验绑定 CanVerify",
            xaml.Contains(@"IsEnabled=""{x:Bind CanVerify, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 打开文件夹有 Click 处理",
            xaml.Contains(@"Click=""OnOpenLocalModelFolderClick""", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 校验有 Click 处理",
            xaml.Contains(@"Click=""OnVerifyLocalModelClick""", StringComparison.Ordinal));

        ChecksContext.Check(
            "本地模型: 打开文件夹走 OpenPath（先查目录是否存在）",
            pageCode.Contains("OpenPath(row.Directory, row.Title)", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 校验走 VerifyLocalModelAsync",
            pageCode.Contains("VerifyLocalModelAsync(row, _lifetime.Token)", StringComparison.Ordinal));
    }

    /// <summary>状态文案必须覆盖 Core 的五态，而不是「已安装 / 未安装」两句。</summary>
    private static void CheckFiveStateStatus(string rowViewModel)
    {
        foreach (var status in new[] { "Installed", "MissingFiles", "SizeMismatch", "ConfigMissing", "NotInstalled" })
        {
            ChecksContext.Check(
                "本地模型: 状态文案覆盖 " + status,
                rowViewModel.Contains("LocalTranslationModelInstallStatus." + status, StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "本地模型: 缺文件文案",
            rowViewModel.Contains("SettingsTranslateLocalModelStatusMissingFiles", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 尺寸不符文案",
            rowViewModel.Contains("SettingsTranslateLocalModelStatusSizeMismatch", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 缺配置文案",
            rowViewModel.Contains("SettingsTranslateLocalModelStatusConfigMissing", StringComparison.Ordinal));
    }

    /// <summary>删除与校验都必须离开 UI 线程，并且删除前有确认。</summary>
    private static void CheckRemoveAndVerifyAreAsync(string settingsViewModel, string pageCode)
    {
        ChecksContext.Check(
            "本地模型: 删除与校验都在 Task.Run 里跑",
            Count(settingsViewModel, "Task.Run(") >= 2,
            "至少删除、校验各一处");
        ChecksContext.Check(
            "本地模型: 删除调用 Remove(row.Kind, () => ReleaseEngines())",
            settingsViewModel.Contains(
                "runtime.Store.Remove(row.Kind, () => runtime.ReleaseEngines())",
                StringComparison.Ordinal),
            "只查 Store.Remove( 不够：上一轮「句柄占着整目录删不动」的回归必须让这条变红");
        ChecksContext.Check(
            "本地模型: 在途翻译时不删除模型",
            settingsViewModel.Contains("AppServices.Translation.Activity.IsActive", StringComparison.Ordinal)
            && settingsViewModel.Contains("SettingsTranslateLocalModelRemoveTranslationBusy", StringComparison.Ordinal),
            "Core 的 Remove 契约要求调用方保证没有在途翻译，缺这条会在翻译进行中拆掉引擎");
        ChecksContext.Check(
            "本地模型: 校验做深校验",
            settingsViewModel.Contains("VerifyInstalled(row.Kind, deep: true)", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 删除前二次确认",
            pageCode.Contains("SettingsTranslateLocalModelDialogRemoveTitle", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 确认后调用异步删除",
            pageCode.Contains("RemoveLocalModelAsync(row, _lifetime.Token)", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 不再手写 row.IsInstalled = true",
            !settingsViewModel.Contains("IsInstalled = true", StringComparison.Ordinal));
    }

    /// <summary>四种删除结果各自有可自证的文案，失败文案会脱敏绝对路径。</summary>
    private static void CheckRemoveMessagesAreSelfProving(string settingsViewModel)
    {
        foreach (var key in new[]
                 {
                     "SettingsTranslateLocalModelStatusRemovedVerified",
                     "SettingsTranslateLocalModelStatusRemovedWithResidue",
                     "SettingsTranslateLocalModelStatusResidueDirectoryOnly",
                     "SettingsTranslateLocalModelStatusRemoveNotFound",
                     "SettingsTranslateLocalModelRemoveFailed",
                 })
        {
            ChecksContext.Check(
                "本地模型: 删除结果文案 " + key,
                settingsViewModel.Contains(key, StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "本地模型: 残留记 \".\" 时说明目录本身未删",
            settingsViewModel.Contains("Residue[0] == \".\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 残留会附上脱敏后的原因",
            settingsViewModel.Contains("SettingsTranslateLocalModelStatusResidueReason", StringComparison.Ordinal),
            "RemovedWithResidue 丢了 result.Error，用户就不知道「为什么删不掉」");
        ChecksContext.Check(
            "本地模型: FormatResidue 传总条数",
            settingsViewModel.Contains("SettingsTranslateLocalModelResidueMore, residue.Count", StringComparison.Ordinal),
            "列 6 项却写「and 7 more」自相矛盾；收尾必须是总数语义");
        ChecksContext.Check(
            "本地模型: 失败文案会去掉绝对路径",
            settingsViewModel.Contains("ToDisplayText(result.Error", StringComparison.Ordinal));
    }

    /// <summary>操作结果不能永久驻留：要有过期时间与清除调用。</summary>
    private static void CheckOperationResultExpires(string settingsViewModel, string rowViewModel)
    {
        ChecksContext.Check(
            "本地模型: 操作文案有寿命",
            settingsViewModel.Contains("LocalModelOperationLifetime", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 到点清空操作文案",
            settingsViewModel.Contains("ExpireOperation(", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 操作文案与安装状态分开",
            rowViewModel.Contains("HasOperationText", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 结束后重新读安装状态",
            settingsViewModel.Contains("GetInstallState(row.Kind)", StringComparison.Ordinal));
    }

    /// <summary>引擎侧口径必须与 UI 一致：用 IsRunnable，不用 IsInstalled。</summary>
    private static void CheckEnginesUseRunnable(string bergamot, string indexTranslate)
    {
        ChecksContext.Check(
            "本地模型: Bergamot 用 IsRunnable",
            bergamot.Contains("GetInstallState(kind).IsRunnable", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: Index-Translate 用 IsRunnable",
            indexTranslate.Contains("GetInstallState(LocalTranslationModelKind.IndexTranslate).IsRunnable", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 引擎不再用 IsInstalled 判可用",
            !bergamot.Contains("_store.IsInstalled(", StringComparison.Ordinal)
            && !indexTranslate.Contains("_store.IsInstalled(", StringComparison.Ordinal));
    }

    /// <summary>
    /// 下载进度必须是「逐字节进度条 + 暂停按钮 + App 层协调器」这一套：
    /// 行内进度条（之前只有一个转圈环，1.31 GB 的单文件下载全程看不见进展）、
    /// 能暂停（暂停后残片留着续传）、下载不跟页面令牌绑定（切页不取消、回来能接上）。
    /// </summary>
    private static void CheckDownloadProgressUi(string xaml, string pageCode, string settingsViewModel)
    {
        ChecksContext.Check(
            "本地模型: 行内有下载进度条",
            xaml.Contains(@"AutomationId=""barLocalModelDownload""", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 进度条绑 DownloadPercent",
            xaml.Contains(@"Value=""{x:Bind DownloadPercent, Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 进度条只在下载时出现",
            xaml.Contains(@"Visibility=""{x:Bind svc:XamlVisibility.FromBool(IsDownloading), Mode=OneWay}""", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 行内有暂停按钮",
            xaml.Contains(@"AutomationId=""btnPauseLocalModel""", StringComparison.Ordinal)
            && xaml.Contains(@"Click=""OnPauseLocalModelClick""", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 暂停按钮绑 CanPause",
            xaml.Contains(@"IsEnabled=""{x:Bind CanPause, Mode=OneWay}""", StringComparison.Ordinal));

        ChecksContext.Check(
            "本地模型: 页面订阅下载进度事件",
            pageCode.Contains("downloads.Changed += OnLocalModelDownloadChanged", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 页面离开时退订下载进度事件",
            pageCode.Contains("LocalModelDownloads?.Changed -= OnLocalModelDownloadChanged", StringComparison.Ordinal),
            "不退订会让已关闭的页面被协调器钉住");
        ChecksContext.Check(
            "本地模型: 进度回调切回 UI 线程再写行",
            pageCode.Contains("DispatcherQueue.TryEnqueue(() => _viewModel.ApplyLocalModelDownload(snapshot))", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 下载改走 App 层协调器",
            pageCode.Contains("_viewModel.StartLocalModelDownload(row)", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 下载不再吃页面生命周期令牌",
            !pageCode.Contains("DownloadLocalModelAsync(row, _lifetime.Token)", StringComparison.Ordinal),
            "页面不缓存，1.31 GB 的下载会被切页取消");
        ChecksContext.Check(
            "本地模型: 进页时接上在途下载",
            pageCode.Contains("_viewModel.ApplyLocalModelDownloads();", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: ViewModel 通过 AppServices.LocalModelDownloads 起下载",
            settingsViewModel.Contains("AppServices.LocalModelDownloads", StringComparison.Ordinal)
            && settingsViewModel.Contains("coordinator.TryStart(row.Kind)", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 进度文案带字节数",
            settingsViewModel.Contains("SettingsTranslateLocalModelDownloadProgress", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 暂停文案",
            settingsViewModel.Contains("SettingsTranslateLocalModelPaused", StringComparison.Ordinal));
        ChecksContext.Check(
            "本地模型: 占用统计算上残片",
            settingsViewModel.Contains("GetStagedBytes(row.Kind)", StringComparison.Ordinal));
    }

    /// <summary>
    /// 行状态机（直接跑被链接进来的行视图模型）：下载中全部按钮禁用、暂停后可续下也可删残片。
    /// 这几条错了不会崩，只在真机上表现为「按钮点了没反应」。
    /// </summary>
    private static void CheckRowDownloadState()
    {
        var row = new LocalModelRowViewModel(
            LocalTranslationModelKind.IndexTranslate,
            Keys.SettingsTranslateLocalModelNotInstalled,
            "自检模型");

        var notInstalled = new LocalTranslationModelState(
            LocalTranslationModelKind.IndexTranslate,
            LocalTranslationModelInstallStatus.NotInstalled,
            @"C:\models\index-translate-2b",
            [],
            [],
            null);
        row.ApplyState(notInstalled, directoryExists: false);
        ChecksContext.Check(
            "本地模型: 未安装时可下载、不可删",
            row.CanDownload && !row.CanRemove,
            "CanDownload=" + row.CanDownload + " CanRemove=" + row.CanRemove);

        row.BeginDownload(43);
        ChecksContext.Check(
            "本地模型: 下载中进度条可见并带百分比",
            row.IsDownloading && row.DownloadPercent == 43,
            "DownloadPercent=" + row.DownloadPercent);
        ChecksContext.Check(
            "本地模型: 下载中不能再点下载 / 删除",
            !row.CanDownload && !row.CanRemove);
        ChecksContext.Check("本地模型: 下载中可暂停", row.CanPause);

        row.EndDownload(hasPartial: true);
        ChecksContext.Check(
            "本地模型: 结束后进度条收起、暂停按钮消失",
            !row.IsDownloading && !row.CanPause);
        ChecksContext.Check(
            "本地模型: 暂停（有残片）后既能续下、也能删掉残片",
            row.CanDownload && row.CanRemove,
            "CanDownload=" + row.CanDownload + " CanRemove=" + row.CanRemove);

        row.EndDownload(hasPartial: false);
        ChecksContext.Check("本地模型: 没有残片又没装好时不可删", !row.CanRemove);
    }

    /// <summary>新增文案中英都要有。</summary>
    private static void CheckLocalizationKeys(string root)
    {
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var path = I18nResw.ResourcesPath(root, language);
            if (!ChecksContext.Check("本地模型: " + language + " 文案文件存在", File.Exists(path), path))
            {
                continue;
            }

            var table = I18nResw.Read(root, language);
            foreach (var key in NewLocalizationKeys)
            {
                ChecksContext.Check(
                    "本地模型: " + language + " 含 " + key,
                    I18nResw.Get(table, key) is not null);
            }
        }

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        var zhMore = I18nResw.Get(zh, "settings.translate.localModelResidueMore");
        var enMore = I18nResw.Get(en, "settings.translate.localModelResidueMore");
        ChecksContext.Check(
            "本地模型: 残留收尾是中英一致的总数语义（7 项 → 等共 7 项 / 7 in total）",
            zhMore == "…等共 {0} 项" && enMore == "… ({0} in total)",
            "MaxItems=6 时列 6 项，收尾若写「and 7 more」就自相矛盾；断言钉死总数语义");
    }

    private static string? ReadRequired(string root, string label, params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        return ChecksContext.Check("本地模型: " + label + " 存在", File.Exists(path), path)
            ? File.ReadAllText(path)
            : null;
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
}
