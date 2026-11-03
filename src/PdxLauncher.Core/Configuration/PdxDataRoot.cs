namespace PdxLauncher.Core.Configuration;

/// <summary>
/// 本启动器自己的数据根目录。
/// <para>
/// 每用户/便携安装：<c>&lt;exeDir&gt;\data-root.txt</c>（逃生口，须为绝对路径）→ <c>&lt;exeDir&gt;\data</c> →
/// <c>&lt;exeDir&gt;\..\PdxNext-data</c>。
/// 每机器安装（Program Files，程序目录不可写）：<c>%LOCALAPPDATA%\PdxNext-Data</c>，
/// 保证每个 Windows 用户有各自可写的设置与缓存；候选都不可写时仍返回
/// <c>&lt;exeDir&gt;\data</c>，由调用方按写失败降级处理，绝不回退到 %TEMP%。
/// </para>
/// </summary>
public static class PdxDataRoot
{
    /// <summary>程序目录下的数据文件夹名。</summary>
    public const string DataFolderName = "data";

    /// <summary>程序目录下可用于改写数据根目录的文件名。</summary>
    public const string OverrideFileName = "data-root.txt";

    /// <summary>安装器写入的安装模式标记（perUser / perMachine）。</summary>
    public const string InstallModeFileName = "install-mode.txt";

    /// <summary>程序目录不可写时的同级回退文件夹名。</summary>
    public const string FallbackFolderName = "PdxNext-data";

    /// <summary>每机器安装时每用户数据的文件夹名（位于 %LOCALAPPDATA% 下）。</summary>
    public const string PerMachineDataFolderName = "PdxNext-Data";

    private static readonly Lazy<string> LazyDirectory =
        new(ResolveForCurrentProcess, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>当前进程使用的数据根目录（首次访问时解析一次，之后固定）。</summary>
    public static string Directory => LazyDirectory.Value;

    /// <summary>读取 <c>&lt;baseDirectory&gt;\data-root.txt</c> 的首个非空行；非法或不存在返回 false。</summary>
    public static bool TryReadOverride(string baseDirectory, out string? directory)
    {
        directory = null;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return false;
        }

        try
        {
            var overridePath = Path.Combine(baseDirectory, OverrideFileName);
            if (!File.Exists(overridePath))
            {
                return false;
            }

            foreach (var line in File.ReadAllLines(overridePath))
            {
                var candidate = line.Trim();
                if (candidate.Length == 0)
                {
                    continue;
                }

                if (!Path.IsPathRooted(candidate))
                {
                    return false;
                }

                directory = Path.GetFullPath(candidate);
                return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            directory = null;
        }

        return false;
    }

    /// <summary>
    /// 按「覆盖文件 → 安装目录 data → 每用户 LocalAppData（仅每机器）→ 同级回退」
    /// 选出第一个可写目录；全部失败时返回 data 路径。
    /// </summary>
    public static string ChooseWritable(string baseDirectory, string? overrideDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var fullBase = Path.GetFullPath(baseDirectory);
        var preferred = Path.Combine(fullBase, DataFolderName);
        var perMachine = IsPerMachineInstall(fullBase);

        var candidates = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(overrideDirectory) && Path.IsPathRooted(overrideDirectory))
        {
            candidates.Add(Path.GetFullPath(overrideDirectory));
        }

        if (!perMachine)
        {
            candidates.Add(preferred);
        }

        if (perMachine)
        {
            candidates.Add(Path.Combine(LocalAppDataDirectory, PerMachineDataFolderName));
        }

        candidates.Add(Path.GetFullPath(Path.Combine(fullBase, "..", FallbackFolderName)));

        // 每机器且 LocalAppData 也不可写时，最后仍尝试安装目录的 data（调用方会按写失败降级）。
        if (perMachine)
        {
            candidates.Add(preferred);
        }

        foreach (var candidate in candidates)
        {
            if (ProbeWritable(candidate))
            {
                return TrimSeparator(candidate);
            }
        }

        return TrimSeparator(preferred);
    }

    /// <summary>当前用户 LocalApplicationData 目录。</summary>
    public static string LocalAppDataDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// 判断程序目录是否属于「每机器安装」：安装器写下的 <c>install-mode.txt</c> 优先；
    /// 没有标记时，Program Files 下或程序目录不可写都视为每机器。
    /// 便携/每用户安装（用户有写权限）继续把数据放在程序目录。
    /// </summary>
    public static bool IsPerMachineInstall(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return false;
        }

        var marker = ReadInstallMode(baseDirectory);
        if (string.Equals(marker, "perMachine", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(marker, "perUser", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(marker, "portable", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fullBase = Path.GetFullPath(baseDirectory);
        if (IsUnderProgramFiles(fullBase))
        {
            return true;
        }

        return !ProbeWritable(fullBase);
    }

    /// <summary>读取安装模式标记；不存在或读失败返回 null。</summary>
    public static string? ReadInstallMode(string baseDirectory)
    {
        try
        {
            var path = Path.Combine(baseDirectory, InstallModeFileName);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsUnderProgramFiles(string fullBase)
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var prefix = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            if (fullBase.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>目录能否创建并写入（写一个探针文件再删掉）。不抛异常。</summary>
    public static bool ProbeWritable(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string ResolveForCurrentProcess()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = System.IO.Directory.GetCurrentDirectory();
        }

        var hasOverride = TryReadOverride(baseDirectory, out var overrideDirectory);
        return ChooseWritable(baseDirectory, hasOverride ? overrideDirectory : null);
    }

    private static string TrimSeparator(string path) =>
        path.Length > 0 && (path.EndsWith('\\') || path.EndsWith('/'))
            ? path.TrimEnd('\\', '/')
            : path;
}
