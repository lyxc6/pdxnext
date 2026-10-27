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
        var fs = new FileSystem();
        var root = FindWorkspaceRoot();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("workspace root: " + root);
        Console.WriteLine();

        var checks = Discover();
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
