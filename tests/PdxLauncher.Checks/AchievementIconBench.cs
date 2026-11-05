namespace PdxLauncher.Checks;

using System.Diagnostics;
using System.Globalization;

using PdxLauncher.Core.Achievements;
using PdxLauncher.Imaging;

/// <summary>
/// 成就图标性能取样：用真实的 CK3 安装目录，测「首次全部生成」与「二次全命中」两段耗时。
/// 只在命令行显式指定时跑（<c>--bench-achievement-icons &lt;install&gt; &lt;cacheDir&gt;</c>），不进常规自检。
/// </summary>
public static class AchievementIconBench
{
    private const int MaxIcons = 198;

    public static int Run(string installRoot, string cacheRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var iconsDir = FindIconsDirectory(installRoot);
        if (iconsDir is null)
        {
            Console.WriteLine("[bench] 找不到成就图标目录: " + installRoot);
            return 2;
        }

        var names = Directory.EnumerateFiles(iconsDir, "*.dds")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static name => name is not null && !name.EndsWith(AchievementIconResolver.LockedSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(static name => name!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .Take(MaxIcons)
            .ToArray();

        // 交替解锁/未解锁，让 _notachieved 变体也走到；两条路径的解码成本相同。
        var work = new List<(string Dds, string Relative)>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            bool unlocked = i % 2 == 0;
            var dds = AchievementIconResolver.ResolveSource(installRoot, names[i], unlocked);
            if (dds is null)
            {
                continue;
            }

            var stem = unlocked ? names[i] : names[i] + "_locked";
            work.Add((dds, Path.Combine("1158310", stem + ".png")));
        }

        if (work.Count == 0)
        {
            Console.WriteLine("[bench] 没有可用的 DDS 图标: " + iconsDir);
            return 2;
        }

        Console.WriteLine("[bench] 图标目录: " + iconsDir);
        Console.WriteLine("[bench] 样本数: " + work.Count.ToString(CultureInfo.InvariantCulture)
            + "，缓存目录: " + cacheRoot);

        // 首次：清空缓存目录，逐个解码落盘，同时记录前 10 行的耗时（首屏实化量级）。
        if (Directory.Exists(cacheRoot))
        {
            Directory.Delete(cacheRoot, recursive: true);
        }

        var cold = new DdsPngCache(cacheRoot, maxDimension: 96);
        var coldWatch = Stopwatch.StartNew();
        int generated = 0;
        long firstTenTicks = 0;
        for (int i = 0; i < work.Count; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var png = cold.Ensure(work[i].Dds, work[i].Relative);
            if (png is not null && File.Exists(png))
            {
                generated++;
            }

            if (i < 10)
            {
                firstTenTicks += Stopwatch.GetTimestamp() - started;
            }
        }

        coldWatch.Stop();

        // 二次：同一个缓存目录、全新实例，应当全部命中且一次都不解码。
        var warm = new DdsPngCache(cacheRoot, maxDimension: 96);
        var warmWatch = Stopwatch.StartNew();
        int hits = 0;
        foreach (var (dds, relative) in work)
        {
            var png = warm.Ensure(dds, relative);
            if (png is not null && File.Exists(png))
            {
                hits++;
            }
        }

        warmWatch.Stop();

        double firstTenMs = firstTenTicks * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine("[bench] 首次全部生成: "
            + coldWatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms"
            + "，生成 " + generated.ToString(CultureInfo.InvariantCulture) + "/" + work.Count.ToString(CultureInfo.InvariantCulture)
            + "，解码次数 " + cold.DecodeCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[bench] 二次全部命中: "
            + warmWatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms"
            + "，命中 " + hits.ToString(CultureInfo.InvariantCulture) + "/" + work.Count.ToString(CultureInfo.InvariantCulture)
            + "，解码次数 " + warm.DecodeCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[bench] 首屏前 10 行解码: "
            + firstTenMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms");

        bool ok = generated == work.Count && hits == work.Count && warm.DecodeCount == 0;
        Console.WriteLine(ok ? "[bench] OK" : "[bench] FAIL");
        return ok ? 0 : 1;
    }

    private static string? FindIconsDirectory(string installRoot)
    {
        string[] layouts =
        [
            Path.Combine(installRoot, "game", "gfx", "interface", "icons", "achievements"),
            Path.Combine(installRoot, "gfx", "interface", "icons", "achievements"),
        ];

        return layouts.FirstOrDefault(Directory.Exists);
    }
}
