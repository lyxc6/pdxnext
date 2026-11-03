namespace PdxLauncher.Core.Steam;

using System.Runtime.Versioning;

using Microsoft.Win32;

/// <summary>
/// 找 Steam 客户端安装目录（判据是 <c>steamapps\libraryfolders.vdf</c> 存在）。
/// <para>
/// 只用于给宿主子进程定位 <c>steam_api64.dll</c> 的兜底候选：游戏目录里那份优先，
/// 其次是用户手填的路径，最后才是 Steam 自带的那份。
/// </para>
/// </summary>
public static class SteamClientLocator
{
    /// <summary>Steam 安装目录里的库列表文件名（用来确认目录真的可用）。</summary>
    public const string LibraryFoldersFileName = "libraryfolders.vdf";

    /// <summary>按「注册表 → 默认安装位置」列出候选的 Steam 安装目录（已存在且校验通过）。</summary>
    public static IReadOnlyList<string> Candidates()
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(path!.Trim());
            }
            catch (Exception)
            {
                return;
            }

            if (seen.Add(normalized) && LooksLikeSteamRoot(normalized))
            {
                results.Add(normalized);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            Add(ReadRegistryPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
            Add(ReadRegistryPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            Add(ReadRegistryPath(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));
        }

        foreach (var root in DefaultRoots())
        {
            Add(root);
        }

        return results;
    }

    /// <summary>Steam 自带的一份 <c>steam_api64.dll</c> 全路径；找不到返回 null。</summary>
    public static string? FindSteamApiDll()
    {
        foreach (var root in Candidates())
        {
            var path = Path.Combine(root, SteamApiLocator.FileName);
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length >= SteamApiLocator.MinimumSizeBytes)
                {
                    return path;
                }
            }
            catch (Exception)
            {
                // 读不到就跳过这个候选。
            }
        }

        return null;
    }

    /// <summary>目录存在且含 <c>steamapps\libraryfolders.vdf</c>。</summary>
    public static bool LooksLikeSteamRoot(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            return Directory.Exists(directory) &&
                File.Exists(Path.Combine(directory!, "steamapps", LibraryFoldersFileName));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<string> DefaultRoots()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Steam");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Steam");
        }

        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        if (!string.IsNullOrWhiteSpace(systemDrive))
        {
            yield return systemDrive + @"\Steam";
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryPath(RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
