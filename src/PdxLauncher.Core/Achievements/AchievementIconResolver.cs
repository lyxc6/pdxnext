namespace PdxLauncher.Core.Achievements;

/// <summary>
/// 成就图标解析：游戏自带的面板图就在安装目录里，无需联网，也无需经过 Steam 宿主。
/// <para>
/// 目录布局有两种（不同游戏版本有差异）：
/// <c>&lt;install&gt;\game\gfx\interface\icons\achievements</c> 与
/// <c>&lt;install&gt;\gfx\interface\icons\achievements</c>。
/// 每个成就一对文件：<c>&lt;apiName&gt;[_notachieved].jpg|png|dds</c>。
/// </para>
/// <para>
/// <see cref="Resolve"/> 只返回 <c>.jpg/.png</c>：返回值会被 WinUI 的 <c>BitmapImage</c>
/// 直接加载，而它不认 DDS。CK3 的 401 个成就图全是 DDS，所以另开
/// <see cref="ResolveSource"/> 一并接受 <c>.dds</c>，由调用方先解码成 PNG 再交给界面；
/// 两者共用同一套「布局 → 未解锁后缀 → 扩展名」优先级，因此不会出现
/// 「<c>Resolve</c> 挑中解锁图、<c>ResolveSource</c> 挑中另一张」的分裂。
/// </para>
/// <para>
/// 未解锁但缺 <c>_notachieved</c> 时选择<b>退回原图</b>而不是灰旗占位：成就行右侧已经明确写「未解锁」，
/// 用成就自己的图仍能让玩家认出是哪一条；只有原图也不存在时才返回 <c>null</c> 交给占位方块。
/// VIC3 的 307 个图标是成对提供的，实际不会触发这条回退，但其它游戏可能只有基础图。
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
        ["gfx", "interface", "icons", "achievements"],
    ];

    /// <summary>界面可直接加载的位图格式，顺序即优先级。</summary>
    private static readonly string[] BitmapExtensions = [".jpg", ".png"];

    /// <summary>可解码的源图格式：位图优先，DDS 只作回退（VIC3 同时有 jpg 和 dds，必须让 jpg 赢）。</summary>
    private static readonly string[] SourceExtensions = [".jpg", ".png", ".dds"];

    /// <summary>未解锁态的命名后缀。</summary>
    public const string LockedSuffix = "_notachieved";

    /// <summary>
    /// 解析某个成就的图标路径，只返回 <c>.jpg/.png</c>（可直接交给 <c>BitmapImage</c>）。
    /// </summary>
    /// <param name="installRoot">游戏安装根目录（<c>GameInstallation.InstallRoot</c>）。</param>
    /// <param name="apiName">成就 id（Steam 的 achievement API name，也就是文件名主干）。</param>
    /// <param name="unlocked">已解锁取原图；未解锁优先取 <c>_notachieved</c>，缺失时退回原图。</param>
    /// <returns>存在的绝对路径；任何一步缺失都返回 <see langword="null"/>。</returns>
    public static string? Resolve(string? installRoot, string? apiName, bool unlocked) =>
        ResolveCore(installRoot, apiName, unlocked, BitmapExtensions);

    /// <summary>
    /// 解析可解码的图标源文件：位图优先，找不到位图时回退到 <c>.dds</c>（CK3 只有 DDS）。
    /// <para>返回值可能是 DDS，调用方必须先转成 PNG 再交给界面，绝不能直接绑给 <c>BitmapImage</c>。</para>
    /// </summary>
    /// <param name="installRoot">游戏安装根目录。</param>
    /// <param name="apiName">成就 id。</param>
    /// <param name="unlocked">已解锁取原图；未解锁优先取 <c>_notachieved</c>。</param>
    /// <returns>存在的绝对路径（jpg/png/dds）；任何一步缺失都返回 <see langword="null"/>。</returns>
    public static string? ResolveSource(string? installRoot, string? apiName, bool unlocked) =>
        ResolveCore(installRoot, apiName, unlocked, SourceExtensions);

    private static string? ResolveCore(string? installRoot, string? apiName, bool unlocked, string[] extensions)
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

        string root;
        try
        {
            root = Path.GetFullPath(installRoot.Trim());
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
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

            if (!Directory.Exists(directory))
            {
                continue;
            }

            if (!unlocked)
            {
                var locked = TryFind(directory, name + LockedSuffix, extensions);
                if (locked is not null)
                {
                    return locked;
                }
            }

            var baseIcon = TryFind(directory, name, extensions);
            if (baseIcon is not null)
            {
                return baseIcon;
            }
        }

        return null;
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

        return null;
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
