namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 找 <c>steam_api64.dll</c>（Valve 的 Steamworks 原生库）。
/// <para>
/// 设计前提：本项目**不分发** Valve 的二进制。可用来源按优先级：
/// ① 用户手填的路径；② 启动器自身目录；③ 开发期的 Steamworks SDK 目录
/// （<c>sdk/redistributable_bin/win64/</c>，从程序目录往上找，最多 6 层）；
/// ④ 当前游戏安装目录（含 <c>binaries</c> / <c>bin</c> 子目录，Paradox 游戏会把
/// DLL 改名成 <c>123456steam_api64.dll</c> 之类，因此用通配匹配）。
/// </para>
/// </summary>
public sealed class SteamApiLocator
{
    /// <summary>标准文件名。</summary>
    public const string FileName = "steam_api64.dll";

    /// <summary>
    /// 可用文件的最小体积。真 DLL 约 300 KB；小于这个数的基本是 0 字节占位或坏文件，
    /// 提前拒掉比让 <c>NativeLibrary.Load</c> 抛 <see cref="BadImageFormatException"/> 更好。
    /// </summary>
    public const long MinimumSizeBytes = 64 * 1024;

    /// <summary>从程序目录向上找 SDK 时最多走几层。</summary>
    private const int MaxParentDepth = 6;

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    public SteamApiLocator(IFileSystem fileSystem, IAppLogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 按优先级列出所有候选路径（去重、保持顺序，**不校验存在性**）。
    /// </summary>
    public IReadOnlyList<string> Candidates(string? settingsPath, string? appDir, GameInstallation? game)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = path!;
            if (seen.Add(normalized))
            {
                results.Add(normalized);
            }
        }

        // ① 用户手填：既可能是 dll 本身，也可能是它所在的目录。
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            var configured = settingsPath!.Trim();
            Add(_fileSystem.DirectoryExists(configured) ? _fileSystem.Combine(configured, FileName) : configured);
        }

        // ② 启动器自身目录。
        if (!string.IsNullOrWhiteSpace(appDir))
        {
            Add(_fileSystem.Combine(appDir!, FileName));
        }

        // ③ 开发期的 Steamworks SDK（仓库根 sdk/redistributable_bin/win64/）。
        foreach (var root in ParentDirectories(appDir))
        {
            Add(_fileSystem.Combine(root, "sdk", "redistributable_bin", "win64", FileName));
        }

        // ④ 当前游戏目录及其常见子目录。
        var installRoot = game?.InstallRoot;
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            Add(_fileSystem.Combine(installRoot!, FileName));
            foreach (var sub in new[] { "binaries", "bin", "steamworks", "Steamworks" })
            {
                var dir = _fileSystem.Combine(installRoot!, sub);
                if (!_fileSystem.DirectoryExists(dir))
                {
                    continue;
                }

                foreach (var match in SafeEnumerate(dir))
                {
                    Add(match);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 返回第一个"存在且体积看起来像真的"的候选；找不到返回 null 并记一条 Warn。
    /// </summary>
    public string? Resolve(string? settingsPath, string? appDir, GameInstallation? game)
    {
        foreach (var candidate in Candidates(settingsPath, appDir, game))
        {
            if (LooksUsable(candidate, out _))
            {
                return candidate;
            }
        }

        _logger.Warn("未找到可用的 steam_api64.dll（候选：" +
            string.Join("；", Candidates(settingsPath, appDir, game)) + "）");
        return null;
    }

    /// <summary>
    /// 判断一个路径是否可用作 Steamworks 原生库；不可用时给出中文原因。
    /// </summary>
    public bool LooksUsable(string? path, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "路径为空";
            return false;
        }

        if (!_fileSystem.FileExists(path!))
        {
            reason = "文件不存在";
            return false;
        }

        var size = _fileSystem.GetFileSize(path!);
        if (size < MinimumSizeBytes)
        {
            reason = "文件体积异常（" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 字节）";
            return false;
        }

        return true;
    }

    /// <summary>程序目录及其各级父目录（去重、跳过空值）。</summary>
    private IEnumerable<string> ParentDirectories(string? appDir)
    {
        var current = string.IsNullOrWhiteSpace(appDir) ? null : _fileSystem.GetFullPath(appDir!);
        var depth = 0;
        while (!string.IsNullOrWhiteSpace(current) && depth++ < MaxParentDepth)
        {
            yield return current!;
            current = _fileSystem.GetDirectoryName(current!);
        }
    }

    /// <summary>枚举目录里所有 <c>*steam_api64.dll</c>；目录不可读时返回空集合，不抛。</summary>
    private IEnumerable<string> SafeEnumerate(string directory)
    {
        try
        {
            return _fileSystem.EnumerateFiles(directory, "*" + FileName, recursive: false).ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn("枚举 steam_api64.dll 候选目录失败：" + directory + "：" + ex.Message);
            return Array.Empty<string>();
        }
    }
}
