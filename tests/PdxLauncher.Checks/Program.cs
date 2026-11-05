namespace PdxLauncher.Checks;

using System.Reflection;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 自检入口。用反射发现所有 <c>public static int Run(IFileSystem)</c> 的检查类，
/// 这样各实现者可以各自新增 *Checks.cs 而无需改动本文件。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        // 自检工程同时充当「假 Steam 宿主」：成就检查类需要验证取消传播、worker 复用、
        // 崩溃重启与回落，就用 --steam-host 把自己拉起来，行为由 FakeSteamHost 按环境变量分派。
        if (args.Length > 0 && string.Equals(args[0], "--steam-host", StringComparison.Ordinal))
        {
            return FakeSteamHost.Run(args);
        }

        var fs = new FileSystem();
        var root = FindWorkspaceRoot();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("workspace root: " + root);

        // 排查单个检查类时用：PdxLauncher.Checks.exe --only SteamAchievementSessionChecks。
        // 全套跑一次要十几分钟，定位挂起/慢用例时不必每次全跑。
        var only = OptionValue(args, "--only");
        Console.WriteLine(only is null ? "检查范围: 全部" : "检查范围: 名字包含 " + only);
        Console.WriteLine();

        var checks = Discover();
        if (only is not null)
        {
            checks = checks
                .Where(c => c.Type.Name.Contains(only, StringComparison.OrdinalIgnoreCase))
                .ToList();
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
