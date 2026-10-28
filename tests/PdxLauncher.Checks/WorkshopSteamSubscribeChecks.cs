namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 「Steam 工坊直连订阅」的非联网自检：dll 定位、可用性判定与 acf 订阅状态解析。
/// <para>
/// 这几段逻辑有个共同的坑：它们读的是本机 Steam 的真实布局（注册表 / 库目录 / appworkshop_*.acf），
/// 一旦写错，界面要么永远显示「未订阅」，要么在没装 Steam 的机器上崩。所以这里用临时目录造数据，
/// 把「订阅了但没下载完」「装了但 acf 里没有」这些分支钉死，不依赖本机状态。
/// </para>
/// <para>纯本地用例，不联网、不调 Steamworks。</para>
/// </summary>
public static class WorkshopSteamSubscribeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckLooksUsable(fs);
        CheckCandidates(fs);
        CheckAcfRead(fs);
        CheckAcfUnknownOnBrokenFile(fs);
        return ChecksContext.Failures - before;
    }

    /// <summary>体积门槛：空文件、不存在的路径都不算可用的 steam_api64.dll。</summary>
    private static void CheckLooksUsable(IFileSystem fs)
    {
        var locator = new SteamApiLocator(fs, new NullLogger());
        var root = NewTempRoot(fs, "looks");

        try
        {
            ChecksContext.Check(
                "空路径判定为不可用",
                !locator.LooksUsable(null, out var reasonForNull) && !string.IsNullOrWhiteSpace(reasonForNull));

            ChecksContext.Check(
                "不存在的 dll 判定为不可用",
                !locator.LooksUsable(fs.Combine(root, "steam_api64.dll"), out var reasonForMissing)
                && !string.IsNullOrWhiteSpace(reasonForMissing));

            var tiny = fs.Combine(root, "tiny", "steam_api64.dll");
            fs.EnsureDirectory(fs.GetDirectoryName(tiny)!);
            fs.WriteAllBytesAtomic(tiny, new byte[1024]);
            ChecksContext.Check(
                "体积不足的 dll 判定为不可用",
                !locator.LooksUsable(tiny, out var reasonForTiny)
                && !string.IsNullOrWhiteSpace(reasonForTiny));

            var full = fs.Combine(root, "full", "steam_api64.dll");
            fs.EnsureDirectory(fs.GetDirectoryName(full)!);
            fs.WriteAllBytesAtomic(full, new byte[128 * 1024]);
            ChecksContext.Check("体积足够的 dll 判定为可用", locator.LooksUsable(full, out var okReason));
            ChecksContext.Check("可用时原因为空", string.IsNullOrEmpty(okReason));
            ChecksContext.Equal("Resolve 取到可用的那份", full, locator.Resolve(full, null, null));
        }
        finally
        {
            SafeDeleteDirectory(fs, root);
        }
    }

    /// <summary>候选顺序：手填 → 程序目录 → 程序目录往上的仓库 sdk/；重复路径只留一份。</summary>
    private static void CheckCandidates(IFileSystem fs)
    {
        var locator = new SteamApiLocator(fs, new NullLogger());
        var root = NewTempRoot(fs, "candidates");
        var appDir = fs.Combine(root, "src", "PdxLauncher.App", "bin", "Debug");
        var manual = fs.Combine(root, "manual", "steam_api64.dll");
        fs.EnsureDirectory(appDir);

        try
        {
            var candidates = locator.Candidates(manual, appDir, null);

            ChecksContext.Equal("第一个候选是手填路径", manual, candidates[0]);
            ChecksContext.Equal(
                "第二个候选是程序目录里的 dll",
                fs.Combine(appDir, "steam_api64.dll"),
                candidates[1]);
            ChecksContext.Check(
                "向上查找时包含仓库 sdk/ 的 redistributable_bin",
                candidates.Any(path => path.EndsWith(
                    fs.Combine("sdk", "redistributable_bin", "win64", "steam_api64.dll"),
                    StringComparison.OrdinalIgnoreCase)));
            ChecksContext.Equal(
                "候选路径不重复",
                candidates.Count,
                candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var noDuplicates = locator.Candidates(manual, null, null);
            ChecksContext.Equal("只给手填路径时只有一条候选", 1, noDuplicates.Count);
        }
        finally
        {
            SafeDeleteDirectory(fs, root);
        }
    }

    /// <summary>acf 解析：订阅+安装、只订阅（下载中）、未订阅、只有内容目录。</summary>
    private static void CheckAcfRead(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "acf");
        var acf = fs.Combine(root, "appworkshop_1158310.acf");
        fs.EnsureDirectory(root);

        const string subscribedAndInstalled = """
            "AppWorkshop"
            {
                "appid" "1158310"
                "WorkshopItemsInstalled"
                {
                    "2517153104"
                    {
                        "size" "1024"
                        "timeupdated" "1700000000"
                        "manifest" "42"
                    }
                }
                "WorkshopItemDetails"
                {
                    "2517153104"
                    {
                        "manifest" "42"
                        "timeupdated" "1700000000"
                        "timetouched" "1700000001"
                        "subscribedby" "76561198000000000"
                    }
                }
            }
            """;

        const string subscribedOnly = """
            "AppWorkshop"
            {
                "appid" "1158310"
                "WorkshopItemDetails"
                {
                    "2517153104"
                    {
                        "manifest" "42"
                        "timeupdated" "1700000000"
                        "timetouched" "1700000001"
                        "subscribedby" "76561198000000000"
                    }
                }
            }
            """;

        try
        {
            fs.WriteAllTextAtomic(acf, subscribedAndInstalled);

            var installedState = WorkshopSubscriptionReader.ReadFromAcf(
                fs,
                new NullLogger(),
                acf,
                "2517153104",
                contentInstalled: true);

            ChecksContext.Equal("已订阅（acf 有 WorkshopItemDetails）", true, installedState.IsSubscribed);
            ChecksContext.Equal("已安装", true, installedState.IsInstalled);
            ChecksContext.Equal("装好了就不算下载中", false, installedState.IsDownloading);
            ChecksContext.Equal("acf 路径带出来了", acf, installedState.AcfPath);
            ChecksContext.Check("状态可判定", installedState.IsKnown);

            // 订阅了但 acf 未记录安装、内容目录也不在 → 正在下载。
            // 单独的 acf 是必要的：上一份已经把该条目记进了 WorkshopItemsInstalled，
            // 那种情况下「已安装」读的是 acf 而不是内容目录，测不到下载中分支。
            var downloadingAcf = fs.Combine(root, "appworkshop_1158310_downloading.acf");
            fs.WriteAllTextAtomic(downloadingAcf, subscribedOnly);

            var downloadingState = WorkshopSubscriptionReader.ReadFromAcf(
                fs,
                new NullLogger(),
                downloadingAcf,
                "2517153104",
                contentInstalled: false);

            ChecksContext.Equal("订阅仍在", true, downloadingState.IsSubscribed);
            ChecksContext.Equal("acf 说没装 → 未安装", false, downloadingState.IsInstalled);
            ChecksContext.Equal("订阅了没装完 → 下载中", true, downloadingState.IsDownloading);

            // acf 里根本没有这个条目：未订阅；内容目录存在时仍能确认「装过」。
            var notSubscribed = WorkshopSubscriptionReader.ReadFromAcf(
                fs,
                new NullLogger(),
                acf,
                "3798537678",
                contentInstalled: false);

            ChecksContext.Equal("acf 里没有该条目 → 未订阅", false, notSubscribed.IsSubscribed);
            ChecksContext.Equal("没有内容目录 → 未安装", false, notSubscribed.IsInstalled);
            ChecksContext.Equal("未订阅时下载状态未知", null, notSubscribed.IsDownloading);

            var contentOnly = WorkshopSubscriptionReader.ReadFromAcf(
                fs,
                new NullLogger(),
                acf,
                "3798537678",
                contentInstalled: true);

            ChecksContext.Equal("只有内容目录 → 未订阅", false, contentOnly.IsSubscribed);
            ChecksContext.Equal("只有内容目录 → 已安装", true, contentOnly.IsInstalled);
        }
        finally
        {
            SafeDeleteDirectory(fs, root);
        }
    }

    /// <summary>
    /// acf 残缺时必须安静地返回 Unknown，不能把界面卡住，更不能谎报「未订阅」。
    /// </summary>
    private static void CheckAcfUnknownOnBrokenFile(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "broken");
        var acf = fs.Combine(root, "appworkshop_949230.acf");
        var emptyAcf = fs.Combine(root, "appworkshop_949230_empty.acf");
        fs.EnsureDirectory(root);

        try
        {
            // 截断的 acf：Valve 的 KV 解析器不会抛，只会解析出空结构。
            fs.WriteAllTextAtomic(acf, "\"AppWorkshop\" { \"appid\" ");

            var state = WorkshopSubscriptionReader.ReadFromAcf(
                fs,
                new NullLogger(),
                acf,
                "74417",
                contentInstalled: false);

            ChecksContext.Equal("坏 acf → 订阅未知", null, state.IsSubscribed);
            ChecksContext.Equal("坏 acf → 安装未知", null, state.IsInstalled);
            ChecksContext.Check("坏 acf 状态不可判定", !state.IsKnown);

            // 能解析但完全没有 AppWorkshop 根节点的文件：同样按未知处理。
            fs.WriteAllTextAtomic(emptyAcf, "\"SomethingElse\" { \"appid\" \"949230\" }");

            var noRoot = WorkshopSubscriptionReader.ReadFromAcf(
                fs,
                new NullLogger(),
                emptyAcf,
                "74417",
                contentInstalled: true);

            ChecksContext.Equal("缺 AppWorkshop 根节点 → 订阅未知", null, noRoot.IsSubscribed);
            ChecksContext.Check("缺 AppWorkshop 根节点 → 不可判定", !noRoot.IsKnown);
        }
        finally
        {
            SafeDeleteDirectory(fs, root);
        }
    }

    private static string NewTempRoot(IFileSystem fs, string name)
    {
        var root = fs.Combine(
            fs.GetFullPath(Path.GetTempPath()),
            "pdxnext-checks",
            "steam-subscribe-" + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        fs.EnsureDirectory(root);
        return root;
    }

    private static void SafeDeleteDirectory(IFileSystem fs, string path)
    {
        try
        {
            if (fs.DirectoryExists(path))
            {
                fs.Delete(path);
            }
        }
        catch
        {
            // 清理失败不影响断言结果。
        }
    }

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
