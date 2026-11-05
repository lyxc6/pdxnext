namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 找 <c>steam_api64.dll</c>（Valve 的 Steamworks 原生库）。
/// <para>
/// 设计前提：本项目**不分发** Valve 的二进制。可用来源按优先级：
/// ① 用户手填的路径；② 启动器自身目录；③ 开发期的 Steamworks SDK 目录
/// （<c>sdk/redistributable_bin/win64/</c>，从程序目录往上找，最多 6 层）；
/// ④ 当前游戏安装目录（含 <c>binaries</c> / <c>bin</c> 子目录）。
/// </para>
/// <para>
/// 每个目录里都**先认原版备份**（<c>steam_api64_o.dll</c> / <c>steam_api64_orig.dll</c> 等），
/// 再认标准名 <c>steam_api64.dll</c>（其次 32 位的 <c>steam_api.dll</c>），最后才是其它同名变体
/// （Paradox 游戏目录里常见 <c>123456steam_api64.dll</c> 这类前缀杂项）。
/// </para>
/// <para>
/// 为什么备份优先：部分玩家用 CreamAPI 之类的 loader 顶替了标准名来做 DLC 解锁，那个 loader
/// 在成就用途上会直接崩（0xC0000005）。原版文件通常被改名成 <c>steam_api64_o.dll</c> 留在同目录，
/// 成就走它才是对的。不删也不改任何文件，只是换用哪一份。
/// </para>
/// <para>
/// 变体之间按「与标准名的字符差」排序，不按字典序 —— 否则 <c>123456…</c> 会排在真 DLL 前面被选中。
/// </para>
/// <para>
/// 「这份到底能不能用于成就」不在主进程判断：真正拉起 <c>--steam-host</c> 子进程做导出表探测与
/// 崩溃回退（见 <c>SteamAchievementSource</c>）；本类只负责给出有序候选。
/// </para>
/// </summary>
public sealed class SteamApiLocator
{
    /// <summary>标准文件名。</summary>
    public const string FileName = "steam_api64.dll";

    /// <summary>32 位标准文件名；只在 64 位精确名缺席时作为降级候选。</summary>
    public const string FallbackFileName = "steam_api.dll";

    /// <summary>
    /// 原版备份的常见文件名。打过 DLC 解锁补丁的游戏会把原版改名成这些、把标准名让给 loader；
    /// 成就用途优先用备份。
    /// </summary>
    public static readonly IReadOnlyList<string> OriginalBackupFileNames = new[]
    {
        "steam_api64_o.dll",
        "steam_api64_orig.dll",
        "steam_api64_original.dll",
        "steam_api_o.dll",
    };

    /// <summary>可用文件的最小体积。真 DLL 约 300 KB；小于这个数的基本是 0 字节占位或坏文件，提前拒掉比让 <c>NativeLibrary.Load</c> 抛 <see cref="BadImageFormatException"/> 更好。</summary>
    public const long MinimumSizeBytes = 64 * 1024;

    /// <summary>从程序目录向上找 SDK 时最多走几层。</summary>
    private const int MaxParentDepth = 6;

    /// <summary>游戏安装根目录下可能放 DLL 的子目录（按这里的顺序试）。</summary>
    private static readonly string[] GameSubDirectories = { "binaries", "bin", "steamworks", "Steamworks" };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="logger">日志。</param>
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
        // 目录要按「备份 → 标准名 → 变体」整目录展开：手填 binaries/ 的玩家多半也打了补丁，
        // 只把 <dir>\steam_api64.dll 放进候选会让 loader 排在原版备份前面，宿主直接 initFailed。
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            var configured = settingsPath!.Trim();
            if (_fileSystem.DirectoryExists(configured))
            {
                AddDirectory(configured);
            }
            else
            {
                Add(configured);
            }
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

        // ④ 当前游戏目录及其常见子目录：原版备份 → 标准名 → 32 位名 → 其它变体（按距离）。
        var installRoot = game?.InstallRoot;
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            AddDirectory(installRoot!);
            foreach (var sub in GameSubDirectories)
            {
                var dir = _fileSystem.Combine(installRoot!, sub);
                if (_fileSystem.DirectoryExists(dir))
                {
                    AddDirectory(dir);
                }
            }
        }

        return results;

        // 同一个目录里，原版备份排在标准名之前：loader 顶替标准名时，成就只有备份能跑。
        // 实测 CK3：game\binaries\steam_api64.dll 是 834,560 B 的 DLC 解锁 loader，宿主动词
        // 返回 initFailed、0 条成就；同目录/根目录的 298,384 B 原版 steam_api64.dll 能正常返回 198 条。
        // 所以成就用途必须先试 _o/orig/original 备份，再试标准名；loader 只适合启动游戏。
        void AddDirectory(string dir)
        {
            foreach (var backup in OriginalBackupFileNames)
            {
                Add(_fileSystem.Combine(dir, backup));
            }

            Add(_fileSystem.Combine(dir, FileName));
            Add(_fileSystem.Combine(dir, FallbackFileName));
            foreach (var match in SafeEnumerate(dir))
            {
                Add(match);
            }
        }
    }

    /// <summary>
    /// 返回第一个「存在且体积像真的」的候选；找不到返回 null 并记一条 Warn。
    /// <para>
    /// 这里**不加载** DLL：能不能用由 <c>--steam-host</c> 子进程的导出表探测与崩溃回退决定，
    /// 主进程碰原生库是之前事故的根源。
    /// </para>
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

    /// <summary>
    /// 枚举目录里所有 <c>*steam_api*.dll</c>（含 <c>steam_api64.dll</c>、<c>steam_api.dll</c>、
    /// <c>123456steam_api64.dll</c>、<c>steam_api64_o.dll</c> 这类变体），按「离标准名的距离」排序；
    /// 目录不可读时返回空集合，不抛。
    /// <para>
    /// 排序口径：文件名与最近的标准名的长度差越小越靠前，同距离时按忽略大小写的字典序。
    /// 这样 <c>steam_api64_o.dll</c>（长度差 2）排在 <c>123456steam_api64.dll</c>（长度差 6）前面，
    /// 不会因为 <c>'1' &lt; 's'</c> 就让前缀杂项抢到位置。
    /// </para>
    /// </summary>
    private IEnumerable<string> SafeEnumerate(string directory)
    {
        try
        {
            // 单条通配就够：*steam_api*.dll 既覆盖 64 位真名与 steam_api.dll，
            // 也覆盖前缀杂项（123456steam_api64.dll）和后缀变体（steam_api64_o.dll）。
            return _fileSystem
                .EnumerateFiles(directory, "*steam_api*.dll", recursive: false)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(VariantDistance)
                .ThenBy(path => _fileSystem.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn("枚举 steam_api64.dll 候选目录失败：" + directory + "：" + ex.Message);
            return Array.Empty<string>();
        }
    }

    /// <summary>文件名与最近的标准名之间的长度差（变体越接近标准名越小）。</summary>
    private int VariantDistance(string path)
    {
        var name = _fileSystem.GetFileName(path);
        var distance = Math.Abs(name.Length - FileName.Length);
        var fallbackDistance = Math.Abs(name.Length - FallbackFileName.Length);
        return Math.Min(distance, fallbackDistance);
    }
}
