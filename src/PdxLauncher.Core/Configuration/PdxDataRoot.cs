namespace PdxLauncher.Core.Configuration;

/// <summary>
/// 本启动器自己的数据根目录：程序目录下的 data\。
/// <para>
/// 解析顺序：<c>&lt;exeDir&gt;\data-root.txt</c>（逃生口，须为绝对路径）→ <c>&lt;exeDir&gt;\data</c> →
/// <c>&lt;exeDir&gt;\..\PdxNext-data</c>（程序目录只读时的同类回退）。
/// 三个候选都不可写时仍返回 <c>&lt;exeDir&gt;\data</c>，由调用方按写失败降级处理；
/// <b>绝不回退到 %LOCALAPPDATA% 或 %TEMP%</b> —— 所有数据必须留在程序目录。
/// </para>
/// </summary>
public static class PdxDataRoot
{
    /// <summary>程序目录下的数据文件夹名。</summary>
    public const string DataFolderName = "data";

    /// <summary>程序目录下可用于改写数据根目录的文件名。</summary>
    public const string OverrideFileName = "data-root.txt";

    /// <summary>程序目录不可写时的同级回退文件夹名。</summary>
    public const string FallbackFolderName = "PdxNext-data";

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

    /// <summary>按「覆盖文件 → data → 同级回退」选出第一个可写目录；全部失败时返回 data 路径。</summary>
    public static string ChooseWritable(string baseDirectory, string? overrideDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var fullBase = Path.GetFullPath(baseDirectory);
        var preferred = Path.Combine(fullBase, DataFolderName);

        var candidates = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(overrideDirectory) && Path.IsPathRooted(overrideDirectory))
        {
            candidates.Add(Path.GetFullPath(overrideDirectory));
        }

        candidates.Add(preferred);
        candidates.Add(Path.GetFullPath(Path.Combine(fullBase, "..", FallbackFolderName)));

        foreach (var candidate in candidates)
        {
            if (ProbeWritable(candidate))
            {
                return TrimSeparator(candidate);
            }
        }

        return TrimSeparator(preferred);
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
