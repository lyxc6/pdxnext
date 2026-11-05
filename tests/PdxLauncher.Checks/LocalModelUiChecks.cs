namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

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

    /// <summary>新增文案中英都要有。</summary>
    private static void CheckLocalizationKeys(string root)
    {
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var path = Path.Combine(root, "tools", "i18n", language, "settings.json");
            if (!ChecksContext.Check("本地模型: " + language + " 文案文件存在", File.Exists(path), path))
            {
                continue;
            }

            var json = File.ReadAllText(path);
            foreach (var key in NewLocalizationKeys)
            {
                ChecksContext.Check(
                    "本地模型: " + language + " 含 " + key,
                    json.Contains("\"" + key + "\"", StringComparison.Ordinal));
            }
        }

        var zhPath = Path.Combine(root, "tools", "i18n", "zh-CN", "settings.json");
        var enPath = Path.Combine(root, "tools", "i18n", "en-US", "settings.json");
        if (File.Exists(zhPath) && File.Exists(enPath))
        {
            var zh = File.ReadAllText(zhPath);
            var en = File.ReadAllText(enPath);
            ChecksContext.Check(
                "本地模型: 残留收尾是中英一致的总数语义（7 项 → 等共 7 项 / 7 in total）",
                zh.Contains("\"settings.translate.localModelResidueMore\": \"…等共 {0} 项\"", StringComparison.Ordinal)
                && en.Contains("\"settings.translate.localModelResidueMore\": \"… ({0} in total)\"", StringComparison.Ordinal),
                "MaxItems=6 时列 6 项，收尾若写「and 7 more」就自相矛盾；断言钉死总数语义");
        }
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
