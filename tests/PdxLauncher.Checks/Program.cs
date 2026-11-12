namespace PdxLauncher.Checks;

using System.Reflection;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Steam;

/// <summary>
/// 自检入口。用反射发现所有 <c>public static int Run(IFileSystem)</c> 的检查类，
/// 这样各实现者可以各自新增 *Checks.cs 而无需改动本文件。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        // 自检工程同时充当「假 Steam 宿主」，两条路径按参数分派：
        //   ① 一次性动词 + --api 路径带 pdxnext-crash / good / initfail / norun 标记 → RunFakeHost：
        //      用来验证「loader 崩掉后换候选」「环境问题不再换候选」等换候选用例；
        //   ② 常驻 serve 以及其余不认识的路径 → FakeSteamHost：行为由 PDXNEXT_FAKE_MODE 决定
        //      （sleep 保持旧用例，ok / ok-delay / crash-first / bad-payload / bad-version / slow-worker）。
        if (args.Length > 0 && string.Equals(args[0], "--steam-host", StringComparison.Ordinal))
        {
            return UseMarkerFakeHost(args) ? RunFakeHost(args) : FakeSteamHost.Run(args);
        }

        // 成就图标性能取样：用真实 CK3 安装目录测「首次全部生成 / 二次全命中」，
        // 与常规自检分开，避免每次 runs 都扫几百个 DDS。
        if (args.Length >= 3 && string.Equals(args[0], "--bench-achievement-icons", StringComparison.Ordinal))
        {
            return AchievementIconBench.Run(args[1], args[2]);
        }

        var fs = new FileSystem();
        var root = FindWorkspaceRoot();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("workspace root: " + root);

        // 排查单个检查类时用：PdxLauncher.Checks.exe --only SteamAchievementsChecks。
        // 全套跑一次要十几分钟，定位挂起/慢用例时不必每次全跑。
        var only = OptionValue(args, "--only");
        Console.WriteLine(only is null ? "检查范围: 全部" : "检查范围: 名字包含 " + only);
        Console.WriteLine();

        var checks = Discover();
        if (only is not null)
        {
            checks = checks.Where(x => x.Type.Name.Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (checks.Count == 0)
        {
            Console.WriteLine("[checks] 没有发现任何检查类");
            return 2;
        }

        var failed = new List<string>();
        foreach (var (type, method) in checks)
        {
            Console.WriteLine("=== " + type.Name + " ===");
            var before = ChecksContext.Failures;
            try
            {
                var rc = (int)(method.Invoke(null, new object?[] { fs }) ?? 1);
                if (rc != 0)
                {
                    failed.Add(type.Name + " (Run 返回 " + rc + ")");
                }
            }
            catch (TargetInvocationException ex)
            {
                Console.WriteLine("  !! 抛出异常: " + ex.InnerException?.GetType().Name + ": " + ex.InnerException?.Message);
                Console.WriteLine(ex.InnerException?.StackTrace);
                failed.Add(type.Name + " (异常)");
            }

            if (ChecksContext.Failures > before)
            {
                failed.Add(type.Name + " (" + (ChecksContext.Failures - before) + " 条断言失败)");
            }

            Console.WriteLine();
        }

        Console.WriteLine("---------------------------------------------");
        Console.WriteLine("检查类: " + checks.Count + "，失败: " + failed.Count + "，断言失败总数: " + ChecksContext.Failures);
        foreach (var f in failed)
        {
            Console.WriteLine("  FAIL " + f);
        }

        return failed.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// <c>--api</c> 指向自检临时目录的候选标记（pdxnext-crash / good / initfail / norun）时走
    /// <see cref="RunFakeHost"/>，其余（尤其是 <c>serve</c>）走按环境变量分派的 <see cref="FakeSteamHost"/>。
    /// <para>
    /// 只认 <c>pdxnext-</c> 前缀：仓库目录本身可能叫 <c>steamapi-initfail</c>，宽匹配会把正常用例误判。
    /// </para>
    /// <para>
    /// <c>api-check</c> 一律走 <see cref="RunFakeHost"/>：它只回答「这份 DLL 能不能读成就」，
    /// 现有用例用路径标记模拟「探测崩溃」；没标记就当合格，让后面的 achievements 真的被调用
    /// （取消传播的用例就是靠它挂住不返回）。
    /// </para>
    /// </summary>
    private static bool UseMarkerFakeHost(string[] args)
    {
        if (args.Length > 1
            && string.Equals(args[1], SteamHostProtocol.ApiCheckVerb, StringComparison.Ordinal))
        {
            return true;
        }

        return HasCandidateMarker(args);
    }

    private static bool HasCandidateMarker(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (!string.Equals(args[i], "--api", StringComparison.Ordinal))
            {
                continue;
            }

            var api = args[i + 1];
            return api.Contains("pdxnext-crash", StringComparison.OrdinalIgnoreCase)
                || api.Contains("pdxnext-good", StringComparison.OrdinalIgnoreCase)
                || api.Contains("pdxnext-initfail", StringComparison.OrdinalIgnoreCase)
                || api.Contains("pdxnext-norun", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static int RunFakeHost(string[] args)
    {
        var verb = args.Length > 1 ? args[1] : string.Empty;
        var api = string.Empty;
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], "--api", StringComparison.Ordinal))
            {
                api = args[i + 1];
            }
        }

        // 只认自检临时目录的固定前缀（pdxnext-*），不要匹配任意路径：
        // 仓库本身就可能位于 ...\steamapi-initfail\ 之类的目录，宽匹配会把正常用例误判成失败。
        var crash = api.Contains("pdxnext-crash", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(verb, SteamHostProtocol.ApiCheckVerb, StringComparison.Ordinal))
        {
            if (crash)
            {
                return CrashExitCode();
            }

            Console.WriteLine(SteamHostProtocol.Encode(SteamHostApiCheckResult.Success()));
            Console.Out.Flush();
            return 0;
        }

        if (string.Equals(verb, SteamHostProtocol.AchievementsVerb, StringComparison.Ordinal))
        {
            if (crash)
            {
                return CrashExitCode();
            }

            // 模拟 DLC 解锁 loader：Init 阶段失败，但进程正常退出、有结果行。
            if (api.Contains("pdxnext-initfail", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(SteamHostProtocol.Encode(
                    SteamHostAchievementsResult.Fail(SteamAchievementCodes.InitFailed, "SteamAPI_Init 失败（假宿主）")));
                Console.Out.Flush();
                return 0;
            }

            // 模拟环境问题：Steam 没启动。换哪份 DLL 都应直接放弃，而不是继续试。
            if (api.Contains("pdxnext-norun", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(SteamHostProtocol.Encode(
                    SteamHostAchievementsResult.Fail(SteamAchievementCodes.SteamNotRunning, "Steam 客户端没有运行（假宿主）")));
                Console.Out.Flush();
                return 0;
            }

            if (api.Contains("pdxnext-good", StringComparison.OrdinalIgnoreCase))
            {
                var items = new[]
                {
                    new SteamHostAchievementItem
                    {
                        Name = "ACH_FAKE",
                        DisplayName = "假成就",
                        Description = "自检用",
                        Hidden = false,
                        Achieved = true,
                        UnlockTime = 0,
                    },
                };
                Console.WriteLine(SteamHostProtocol.Encode(SteamHostAchievementsResult.Success(items)));
                Console.Out.Flush();
                return 0;
            }

            // 其余情况挂住不返回，供父进程验证「调用方取消」。
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(30));
            return 1;
        }

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(30));
        return 1;
    }

    /// <summary>模拟原生访问违例（0xC0000005 的 int 形式），让父进程看到崩溃退出码。</summary>
    private static int CrashExitCode() => unchecked((int)0xC0000005);

    private static List<(Type Type, MethodInfo Method)> Discover()
    {
        var found = new List<(Type, MethodInfo)>();
        var assembly = typeof(Program).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsClass || !type.IsAbstract || !type.IsSealed || !type.IsPublic)
            {
                continue;
            }

            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IFileSystem) }, null);
            if (method is not null && method.ReturnType == typeof(int))
            {
                found.Add((type, method));
            }
        }

        return found.OrderBy(x => x.Item1.Name, StringComparer.Ordinal).ToList();
    }

    private static string? OptionValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
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
