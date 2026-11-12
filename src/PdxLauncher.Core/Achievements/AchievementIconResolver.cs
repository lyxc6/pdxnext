namespace PdxLauncher.Core.Achievements;

/// <summary>
/// 成就图标解析：游戏自带的面板图就在安装目录里，无需联网，也无需经过 Steam 宿主。
/// <para>
/// 各游戏的目录布局并不统一，按下面的顺序逐个尝试（只有存在的目录才会被查）：
/// <list type="bullet">
/// <item><c>&lt;install&gt;\game\gfx\interface\icons\achievements</c>：CK3 / VIC3 / Imperator</item>
/// <item><c>&lt;install&gt;\game\main_menu\gfx\interface\icons\achievements</c>：EU5</item>
/// <item><c>&lt;install&gt;\gfx\interface\icons\achievements</c>：Stellaris</item>
/// <item><c>&lt;install&gt;\gfx\interface\achievements</c>：EU4 / CK2</item>
/// <item><c>&lt;install&gt;\gfx\achievements</c>：HOI4</item>
/// <item><c>&lt;install&gt;\Cities2_Data\Content\Game\UI\Media\Game\Achievements</c>：CS2（PNG）</item>
/// </list>
/// 每个成就一对文件：<c>&lt;apiName&gt;[_notachieved|_locked|_grey].jpg|png|dds</c>。
/// </para>
/// <para>
/// <see cref="Resolve"/> 只返回 <c>.jpg/.png</c>：返回值会被 WinUI 的 <c>BitmapImage</c>
/// 直接加载，而它不认 DDS。CK3 的成就图全是 DDS，所以另开
/// <see cref="ResolveSource"/> 一并接受 <c>.dds</c>，由调用方先解码成 PNG 再交给界面；
/// 两者共用同一套「布局 → 未解锁后缀 → 扩展名」优先级，因此不会出现
/// 「<c>Resolve</c> 挑中解锁图、<c>ResolveSource</c> 挑中另一张」的分裂。
/// </para>
/// <para>
/// 未解锁但缺灰图时（EU4 只提供彩色原图）选择<b>退回原图</b>而不是灰旗占位：成就行右侧已经明确写
/// 「未解锁」，用成就自己的图仍能让玩家认出是哪一条；调用方再按
/// <see cref="AchievementIconSource.IsLockedArt"/> 判断要不要压暗。只有原图也不存在时才返回
/// <c>null</c> 交给占位方块。
/// </para>
/// <para>
/// 纯函数：只读磁盘存在性，不缓存、不抛异常、不写日志。任何非法输入、
/// 不存在的目录或文件都返回 <c>null</c>，由界面继续显示占位方块。
/// </para>
/// </summary>
public static class AchievementIconResolver
{
    /// <summary>按优先级尝试的图标目录（相对安装根）。</summary>
    private static readonly string[][] Layouts =
    [
        ["game", "gfx", "interface", "icons", "achievements"],
        ["game", "main_menu", "gfx", "interface", "icons", "achievements"],
        ["gfx", "interface", "icons", "achievements"],
        ["gfx", "interface", "achievements"],
        ["gfx", "achievements"],
        ["Cities2_Data", "Content", "Game", "UI", "Media", "Game", "Achievements"],
    ];

    /// <summary>API 名与文件名之间常见的前缀差异（带 / 不带）。</summary>
    private static readonly string[] NamePrefixes = ["achievement_", "ach_"];

    /// <summary>界面可直接加载的位图格式，顺序即优先级。</summary>
    private static readonly string[] BitmapExtensions = [".jpg", ".png"];

    /// <summary>可解码的源图格式：位图优先，DDS 只作回退（VIC3 同时有 jpg 和 dds，必须让 jpg 赢）。</summary>
    private static readonly string[] SourceExtensions = [".jpg", ".png", ".dds"];

    /// <summary>最常见的未解锁后缀（VIC3 / EU5）。保留单独常量给历史上的调用方。</summary>
    public const string LockedSuffix = "_notachieved";

    /// <summary>
    /// 未解锁态的命名后缀，按优先级尝试：<c>_notachieved</c>（VIC3 / EU5）、
    /// <c>_locked</c>（Stellaris / CS2）、<c>_grey</c>（HOI4）。
    /// HOI4 的 <c>_not_eligible</c> 故意不收：它表示「当前规则下不可达成」，不是「未解锁」。
    /// </summary>
    public static readonly string[] LockedSuffixes = ["_notachieved", "_locked", "_grey"];

    /// <summary>
    /// 解析某个成就的图标路径，只返回 <c>.jpg/.png</c>（可直接交给 <c>BitmapImage</c>）。
    /// </summary>
    /// <param name="installRoot">游戏安装根目录（<c>GameInstallation.InstallRoot</c>）。</param>
    /// <param name="apiName">成就 id（Steam 的 achievement API name，也就是文件名主干）。</param>
    /// <param name="unlocked">已解锁取原图；未解锁优先取灰图，缺失时退回原图。</param>
    /// <returns>存在的绝对路径；任何一步缺失都返回 <see langword="null"/>。</returns>
    public static string? Resolve(string? installRoot, string? apiName, bool unlocked) =>
        ResolveCore(installRoot, apiName, unlocked, BitmapExtensions)?.Path;

    /// <summary>
    /// 解析可解码的图标源文件：位图优先，找不到位图时回退到 <c>.dds</c>（CK3 只有 DDS）。
    /// <para>返回值可能是 DDS，调用方必须先转成 PNG 再交给界面，绝不能直接绑给 <c>BitmapImage</c>。</para>
    /// </summary>
    /// <param name="installRoot">游戏安装根目录。</param>
    /// <param name="apiName">成就 id。</param>
    /// <param name="unlocked">已解锁取原图；未解锁优先取灰图。</param>
    /// <returns>存在的绝对路径（jpg/png/dds）；任何一步缺失都返回 <see langword="null"/>。</returns>
    public static string? ResolveSource(string? installRoot, string? apiName, bool unlocked) =>
        ResolveCore(installRoot, apiName, unlocked, SourceExtensions)?.Path;

    /// <summary>
    /// 与 <see cref="ResolveSource"/> 相同，但额外告诉调用方命中的是不是「未解锁」专用图：
    /// 为 <see langword="false"/> 说明游戏没有灰图、退回了原图，界面可以自行压暗来区分状态。
    /// </summary>
    public static AchievementIconSource? ResolveSourceDetailed(string? installRoot, string? apiName, bool unlocked) =>
        ResolveCore(installRoot, apiName, unlocked, SourceExtensions);

    /// <summary>
    /// 按同一份布局清单找出该游戏实际使用的图标目录（第一个存在的）。
    /// 给基准测试 / 诊断复用，避免布局清单在多处各写一份而漂移。
    /// </summary>
    /// <returns>存在的绝对目录路径；一个都不存在时返回 <see langword="null"/>。</returns>
    public static string? FindIconDirectory(string? installRoot)
    {
        foreach (var directory in CandidateDirectories(installRoot))
        {
            if (SafeDirectoryExists(directory))
            {
                return directory;
            }
        }

        return null;
    }

    private static AchievementIconSource? ResolveCore(
        string? installRoot,
        string? apiName,
        bool unlocked,
        string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(apiName))
        {
            return null;
        }

        var name = apiName.Trim();
        if (!IsSafeFileName(name))
        {
            return null;
        }

        var stems = NameStems(name).ToArray();

        foreach (var directory in CandidateDirectories(installRoot))
        {
            if (!SafeDirectoryExists(directory))
            {
                continue;
            }

            if (!unlocked)
            {
                foreach (var stem in stems)
                {
                    foreach (var suffix in LockedSuffixes)
                    {
                        var locked = TryFind(directory, stem + suffix, extensions);
                        if (locked is not null)
                        {
                            return new AchievementIconSource(locked, IsLockedArt: true);
                        }
                    }
                }
            }

            foreach (var stem in stems)
            {
                var baseIcon = TryFind(directory, stem, extensions);
                if (baseIcon is not null)
                {
                    return new AchievementIconSource(baseIcon, IsLockedArt: false);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 按优先级列出该游戏可能的图标目录（不校验存在性）。
    /// 安装根本身不合法时返回空表，调用方据此直接放弃。
    /// </summary>
    private static IEnumerable<string> CandidateDirectories(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            yield break;
        }

        string root;
        try
        {
            root = Path.GetFullPath(installRoot.Trim());
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            yield break;
        }

        foreach (var layout in Layouts)
        {
            string directory;
            try
            {
                directory = Path.Combine([root, .. layout]);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            yield return directory;
        }
    }

    /// <summary>
    /// 文件名的候选写法：先原样，再补 / 去已知前缀（<c>achievement_</c>、<c>ach_</c>）。
    /// Steam API 名与文件名并不总是逐字一致：HOI4 的 API 名可能带 <c>ACHIEVEMENT_</c>，
    /// CS2 是 <c>ACH_ALITTLEBITOFTLC</c> 对文件名 <c>ALittleBitofTLC</c>，两种写法都试一遍最稳。
    /// </summary>
    private static IEnumerable<string> NameStems(string name)
    {
        yield return name;

        var stripped = false;
        foreach (var prefix in NamePrefixes)
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return name[prefix.Length..];
                stripped = true;
                break;
            }
        }

        if (stripped)
        {
            yield break;
        }

        foreach (var prefix in NamePrefixes)
        {
            if (name.Length + prefix.Length <= 200)
            {
                yield return prefix + name;
            }
        }
    }

    private static string? TryFind(string directory, string stem, string[] extensions)
    {
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(directory, stem + extension);
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
            {
                // 路径不可读就当作没有图标，绝不让一个坏文件名把整页带崩。
            }
        }

        return TryFindNormalized(directory, stem, extensions);
    }

    /// <summary>
    /// 精确文件名没命中时的兜底：忽略大小写与分隔符再比一次。
    /// 覆盖 <c>ALittleBitofTLC</c> ↔ <c>A_Little_Bit_of_TLC</c> 这种「同一名字、两种写法」的情况；
    /// 只做完全相等比较，不做包含匹配，免得把彩色原图误判成未解锁灰图。
    /// 只在精确查找失败时枚举目录，正常路径不产生额外 IO。
    /// </summary>
    private static string? TryFindNormalized(string directory, string stem, string[] extensions)
    {
        var target = NormalizeName(stem);
        if (target.Length < 3)
        {
            return null;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        try
        {
            foreach (var file in files)
            {
                if (!HasExtension(file, extensions))
                {
                    continue;
                }

                if (string.Equals(NormalizeName(Path.GetFileNameWithoutExtension(file)), target, StringComparison.Ordinal))
                {
                    return file;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 枚举中途目录被删 / 无权限：当作没有图标。
        }

        return null;
    }

    private static bool HasExtension(string path, string[] extensions)
    {
        foreach (var extension in extensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeName(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static bool SafeDirectoryExists(string directory)
    {
        try
        {
            return Directory.Exists(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>只允许单段文件名：拒绝分隔符、盘符与 <c>..</c> 之类的路径成分。</summary>
    private static bool IsSafeFileName(string name)
    {
        if (name.Length is 0 or > 200)
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return name.IndexOfAny(['/', '\\', ':']) < 0
            && !string.Equals(name, ".", StringComparison.Ordinal)
            && !string.Equals(name, "..", StringComparison.Ordinal);
    }
}

/// <summary>
/// 解析到的成就图标源文件。
/// <para>
/// <see cref="IsLockedArt"/> 为 <see langword="true"/> 表示命中的是「未解锁」专用图（<c>_notachieved</c>
/// / <c>_locked</c> / <c>_grey</c>）；为 <see langword="false"/> 表示未解锁但游戏没有灰图、退回了原图，
/// 界面可以再压暗一档来区分。
/// </para>
/// </summary>
/// <param name="Path">图标文件的绝对路径（可能是 DDS）。</param>
/// <param name="IsLockedArt">是否命中了未解锁专用图。</param>
public readonly record struct AchievementIconSource(string Path, bool IsLockedArt);
