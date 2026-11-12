namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Steam;

/// <summary>
/// T2 自检：DLC <b>内容</b>来源的判定链路 —— <c>appmanifest</c> 的 <c>buildid</c> / <c>InstalledDepots</c>
/// 能不能读出来，以及 <see cref="DlcContentInspector"/> 的四条优先级。
/// <para>
/// 钉住的边界（都是真机上会咬人的）：
/// <list type="bullet">
/// <item>清单里没有 <c>InstalledDepots</c> 段 ≠ 一个 DLC 都没装：必须表达成「不知道」，
/// 否则人工拷进去的 DLC 会被标成 Steam 装的、Steam 装的会被标成没装。</item>
/// <item>本体 depot（无 <c>dlcappid</c>）不参与 DLC 判定。</item>
/// <item>只有 <c>dlc_metadata/</c> 元数据的条目是「内容未安装」；CS2 那种 <c>steam:&lt;appid&gt;</c> 的
/// 伪造路径在清单没登记时只能说「不知道」，不能报「缺内容」。</item>
/// </list>
/// </para>
/// <para>全部用一次性临时目录自造 fixture，不碰真机安装目录。</para>
/// </summary>
public static class DlcContentChecks
{
    private const int Victoria3AppId = 529340;
    private const int Victoria3DepotAppId = 2071470;
    private const int Victoria3DlcAppId = 2071471;
    private const int StateAndRevolutionAppId = 4314660;

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckManifestDepots(fs);
        CheckManifestWithoutDepots(fs);
        CheckManifestMissing(fs);
        CheckClassifyRules();
        CheckInspectorMap();
        return ChecksContext.Failures - before;
    }

    // ------------------------------------------------------------------ appmanifest

    private static void CheckManifestDepots(IFileSystem fs)
    {
        Console.WriteLine("[DlcContentChecks] appmanifest 的 buildid 与 InstalledDepots");
        var root = PlaysetChecks.CreateTempRoot(fs, "dlccontent-depots");
        try
        {
            var library = Path.Combine(root, "lib");
            var steamApps = Path.Combine(library, "steamapps");
            fs.EnsureDirectory(steamApps);
            fs.WriteAllTextAtomic(
                Path.Combine(steamApps, "appmanifest_" + Victoria3AppId + ".acf"),
                Acf(includeDepots: true));

            var manifest = new AppManifestReader(fs, new NullLogger()).ReadForApp(library, Victoria3AppId);

            ChecksContext.Check("能读到带 InstalledDepots 的清单", manifest is not null);
            if (manifest is null)
            {
                return;
            }

            ChecksContext.Equal("buildid 解析", "26453692", manifest.BuildId);
            ChecksContext.Check("有 InstalledDepots 段时 KnowsDlcDepots", manifest.KnowsDlcDepots);
            ChecksContext.Equal("depot 条目数（本体 + 1 个 DLC）", 2, manifest.InstalledDepots?.Count ?? -1);
            ChecksContext.Equal("DLC appid 只有 1 个", 1, manifest.InstalledDlcAppIds.Count);
            ChecksContext.Check(
                "dlcappid 落在集合里",
                manifest.InstalledDlcAppIds.Contains(Victoria3DlcAppId));
            ChecksContext.Check(
                "本体 depot 不算 DLC",
                !manifest.InstalledDlcAppIds.Contains(Victoria3AppId));

            var dlcDepot = manifest.InstalledDepots?.FirstOrDefault(static d => d.DlcAppId is not null);
            ChecksContext.Equal("DLC depot 的 size", 630_473_793L, dlcDepot?.Size ?? -1);
            ChecksContext.Equal("DLC depot 的 id", Victoria3DepotAppId, dlcDepot?.DepotId ?? -1);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckManifestWithoutDepots(IFileSystem fs)
    {
        Console.WriteLine("[DlcContentChecks] 缺 InstalledDepots 段 = 不知道，不是「没装」");
        var root = PlaysetChecks.CreateTempRoot(fs, "dlccontent-nodepots");
        try
        {
            var library = Path.Combine(root, "lib");
            var steamApps = Path.Combine(library, "steamapps");
            fs.EnsureDirectory(steamApps);
            fs.WriteAllTextAtomic(
                Path.Combine(steamApps, "appmanifest_" + Victoria3AppId + ".acf"),
                Acf(includeDepots: false));

            var manifest = new AppManifestReader(fs, new NullLogger()).ReadForApp(library, Victoria3AppId);

            ChecksContext.Check("缺段时照样能读到清单", manifest is not null);
            ChecksContext.Check("缺段时 KnowsDlcDepots 为 false", manifest?.KnowsDlcDepots == false);
            ChecksContext.Equal("缺段时 DLC appid 集合为空", 0, manifest?.InstalledDlcAppIds.Count ?? -1);
            ChecksContext.Equal("缺段时 buildid 仍可读", "26453692", manifest?.BuildId);

            // 空段与缺段必须区分：`"InstalledDepots" { }` 是「确实一个都没装」。
            var emptyLibrary = Path.Combine(root, "empty-lib");
            var emptySteamApps = Path.Combine(emptyLibrary, "steamapps");
            fs.EnsureDirectory(emptySteamApps);
            fs.WriteAllTextAtomic(
                Path.Combine(emptySteamApps, "appmanifest_" + Victoria3AppId + ".acf"),
                AcfEmptyDepots());
            var emptyManifest = new AppManifestReader(fs, new NullLogger())
                .ReadForApp(emptyLibrary, Victoria3AppId);
            ChecksContext.Check("空段时 KnowsDlcDepots 为 true", emptyManifest?.KnowsDlcDepots == true);
            ChecksContext.Equal("空段时集合为空", 0, emptyManifest?.InstalledDlcAppIds.Count ?? -1);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckManifestMissing(IFileSystem fs)
    {
        Console.WriteLine("[DlcContentChecks] 清单不存在时返回 null（界面不许宣称 Steam 已装）");
        var reader = new AppManifestReader(fs, new NullLogger());
        ChecksContext.Check(
            "不存在的清单返回 null",
            reader.Read(Path.Combine(Path.GetTempPath(), "pdxnext-no-such-appmanifest-529340.acf")) is null);
    }

    // ------------------------------------------------------------------ 判定规则

    private static void CheckClassifyRules()
    {
        Console.WriteLine("[DlcContentChecks] 内容状态的四条优先级");

        var installed = new HashSet<int> { Victoria3DlcAppId };

        // ① 清单登记了这个 dlcappid → Steam 装的（哪怕磁盘上描述文件的路径长什么样）
        ChecksContext.Equal(
            "命中 dlcappid → SteamInstalled",
            DlcContentState.SteamInstalled,
            DlcContentInspector.Classify(
                Victoria3DlcAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "dlc001_preorder/dlc001.dlc",
                installed));

        // ② 没登记但有磁盘描述文件 → 人工拷的
        ChecksContext.Equal(
            "没登记但有描述文件 → FilesPresent",
            DlcContentState.FilesPresent,
            DlcContentInspector.Classify(
                StateAndRevolutionAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "dlc019_ip5/dlc019_ip5.dlc",
                installed));

        // ③ 只有元数据 → 内容没落盘
        ChecksContext.Equal(
            "只有 dlc_metadata → Missing",
            DlcContentState.Missing,
            DlcContentInspector.Classify(
                StateAndRevolutionAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "dlc_metadata/dlc019",
                installed));

        // ④ 没有 steam_id → 判不了
        ChecksContext.Equal(
            "没有 steam_id → Unknown",
            DlcContentState.Unknown,
            DlcContentInspector.Classify(null, "dlc019_ip5/dlc019_ip5.dlc", installed));
        ChecksContext.Equal(
            "steam_id 非数字 → Unknown",
            DlcContentState.Unknown,
            DlcContentInspector.Classify("abc", "dlc019_ip5/dlc019_ip5.dlc", installed));

        // 清单不可用（null）时绝不能判出 SteamInstalled：没读到 ≠ 没装。
        ChecksContext.Equal(
            "清单不可用时退化成 FilesPresent",
            DlcContentState.FilesPresent,
            DlcContentInspector.Classify(
                Victoria3DlcAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "dlc001_preorder/dlc001.dlc",
                steamInstalledDlcAppIds: null));

        // CS2 的伪造路径（steam:<appid>）：清单没登记时只能说不知道，不能报缺内容。
        ChecksContext.Equal(
            "steam: 前缀未登记 → Unknown",
            DlcContentState.Unknown,
            DlcContentInspector.Classify("4544310", "steam:4544310", installed));
        ChecksContext.Equal(
            "steam: 前缀已登记 → SteamInstalled",
            DlcContentState.SteamInstalled,
            DlcContentInspector.Classify(
                Victoria3DlcAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "steam:" + Victoria3DlcAppId,
                installed));
    }

    private static void CheckInspectorMap()
    {
        Console.WriteLine("[DlcContentChecks] 批量判定的 key 与去重");

        var installed = new HashSet<int> { Victoria3DlcAppId };
        var dlcs = new[]
        {
            Dlc(StateAndRevolutionAppId, "dlc019_ip5/dlc019_ip5.dlc"),
            Dlc(StateAndRevolutionAppId, "dlc_metadata/dlc019"),
            Dlc(Victoria3DlcAppId, "dlc001_preorder/dlc001.dlc"),

            // 同一路径出现两次只留第一条（与 DlcCatalog 的去重口径一致）
            Dlc(StateAndRevolutionAppId, "dlc019_ip5/dlc019_ip5.dlc"),
        };

        var map = DlcContentInspector.Inspect(dlcs, manifest: null);
        ChecksContext.Equal("key 数（去重后）", 3, map.Count);
        ChecksContext.Check(
            "磁盘描述文件 → FilesPresent",
            map["dlc019_ip5/dlc019_ip5.dlc"] == DlcContentState.FilesPresent);
        ChecksContext.Check(
            "只有元数据 → Missing",
            map["dlc_metadata/dlc019"] == DlcContentState.Missing);
        ChecksContext.Check(
            "清单为 null 时不会 SteamInstalled",
            map["dlc001_preorder/dlc001.dlc"] == DlcContentState.FilesPresent);

        var withManifest = DlcContentInspector.Inspect(
            dlcs,
            new AppManifest(
                Victoria3AppId,
                "Victoria 3",
                "Victoria 3",
                @"D:\app\Steam\steamapps\common\Victoria 3",
                4,
                1,
                InstalledDepots: new[] { new AppDepot(Victoria3DepotAppId, Victoria3DlcAppId, 630_473_793L) }));
        ChecksContext.Check(
            "清单登记后同一路径变 SteamInstalled",
            withManifest["dlc001_preorder/dlc001.dlc"] == DlcContentState.SteamInstalled);

        ChecksContext.Equal("空列表返回空表", 0, DlcContentInspector.Inspect(Array.Empty<DlcInfo>(), null).Count);
    }

    // ------------------------------------------------------------------ 构建工具

    private static DlcInfo Dlc(int steamId, string relativePath) => new(
        DisplayName: "DLC " + steamId,
        PopsId: string.Empty,
        SteamId: steamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FilePathRelativeToDlcPath: relativePath,
        ThumbnailPath: string.Empty,
        Description: string.Empty,
        RawBytes: Array.Empty<byte>());

    /// <summary>一份形状与真机一致的 Victoria 3 清单（含本体 depot 与一个 DLC depot）。</summary>
    private static string Acf(bool includeDepots)
    {
        var depots = includeDepots
            ? "\t\t\"529341\"\n\t\t{\n\t\t\t\"manifest\"\t\t\"1111111111111111111\"\n\t\t\t\"size\"\t\t\"12562973323\"\n\t\t}\n"
              + "\t\t\"" + Victoria3DepotAppId + "\"\n\t\t{\n\t\t\t\"manifest\"\t\t\"2222222222222222222\"\n\t\t\t\"size\"\t\t\"630473793\"\n"
              + "\t\t\t\"dlcappid\"\t\t\"" + Victoria3DlcAppId + "\"\n\t\t}\n"
            : string.Empty;

        return "\"AppState\"\n{\n"
            + "\t\"appid\"\t\t\"" + Victoria3AppId + "\"\n"
            + "\t\"name\"\t\t\"Victoria 3\"\n"
            + "\t\"installdir\"\t\t\"Victoria 3\"\n"
            + "\t\"StateFlags\"\t\t\"4\"\n"
            + "\t\"SizeOnDisk\"\t\t\"13333333333\"\n"
            + "\t\"buildid\"\t\t\"26453692\"\n"
            + (includeDepots ? "\t\"InstalledDepots\"\n\t{\n" + depots + "\t}\n" : string.Empty)
            + "}\n";
    }

    private static string AcfEmptyDepots() =>
        "\"AppState\"\n{\n"
        + "\t\"appid\"\t\t\"" + Victoria3AppId + "\"\n"
        + "\t\"installdir\"\t\t\"Victoria 3\"\n"
        + "\t\"StateFlags\"\t\t\"4\"\n"
        + "\t\"InstalledDepots\"\n\t{\n\t}\n"
        + "}\n";

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
