namespace PdxLauncher.Core.Playsets;

using PdxLauncher.Core.Contracts;

/// <summary>Mod 库一行的三角提示属于哪一类（<see cref="None"/> 表示不显示三角）。</summary>
public enum ModWarningKind
{
    /// <summary>没有问题，不显示三角。</summary>
    None,

    /// <summary>磁盘扫到、官方 mod 库里没有登记，且没有可用的创意工坊编号。</summary>
    Unregistered,

    /// <summary>创意工坊订阅来的、官方 mod 库里还没有登记。</summary>
    UnregisteredWorkshop,

    /// <summary>mods.status 不是 <see cref="ModLibraryWarnings.ReadyStatus"/>。</summary>
    Status,

    /// <summary>声明的支持版本低于当前游戏版本。</summary>
    VersionBelow,
}

/// <summary>「这个 mod 声明支持哪个版本」与当前游戏版本对照后的结论。</summary>
public enum ModVersionSupport
{
    /// <summary>没声明（requiredVersion 为空，或写成 "1.*.*" / "*" 这类读不出两段数字的形式）。</summary>
    Undeclared,

    /// <summary>声明了版本，但当前游戏版本未知或读不出两段数字。</summary>
    DeclaredUnknownGame,

    /// <summary>声明的版本不低于当前游戏版本。</summary>
    NotBelow,

    /// <summary>声明的版本低于当前游戏版本。</summary>
    Below,
}

/// <summary>
/// Mod 库一行三角提示的判定结果：三角要不要出现，以及三段文案各取哪个键。
/// 界面只负责把键翻成文案，判定与文案键都在这里，自检（ModLibraryWarningChecks）能直接覆盖。
/// </summary>
/// <param name="Kind">三角的类别；<see cref="Playsets.ModWarningKind.None"/> 时不显示三角。</param>
/// <param name="VersionSupport">版本对照结论。</param>
/// <param name="RequiredVersion">mods.requiredVersion 原文（已 trim）；未声明时为空串。</param>
/// <param name="GameVersion">当前游戏版本原文（已 trim）；未知时为空串。</param>
/// <param name="Status">mods.status 原文（已 trim）。</param>
public sealed record ModWarningInfo(
    ModWarningKind Kind,
    ModVersionSupport VersionSupport,
    string RequiredVersion,
    string GameVersion,
    string Status)
{
    /// <summary>没有问题时的结果。</summary>
    public static readonly ModWarningInfo None = new(
        ModWarningKind.None,
        ModVersionSupport.Undeclared,
        string.Empty,
        string.Empty,
        string.Empty);

    /// <summary>要不要显示三角。</summary>
    public bool HasTriangle => Kind != ModWarningKind.None;

    /// <summary>
    /// 版本支持说明的文案键（三角文案的第一句）；没有三角时为 null。
    /// </summary>
    public string? VersionKey => !HasTriangle
        ? null
        : VersionSupport switch
        {
            ModVersionSupport.Below or ModVersionSupport.NotBelow => ModLibraryWarnings.KeyVersionDeclared,
            ModVersionSupport.DeclaredUnknownGame => ModLibraryWarnings.KeyVersionDeclaredUnknownGame,
            _ => ModLibraryWarnings.KeyVersionUndeclared,
        };

    /// <summary>
    /// 对照结论的文案键（三角文案的第二句）；说不出结论（未声明 / 游戏版本未知）或没有三角时为 null。
    /// </summary>
    public string? ConclusionKey => !HasTriangle
        ? null
        : VersionSupport switch
        {
            ModVersionSupport.Below => ModLibraryWarnings.KeyVersionBelow,
            ModVersionSupport.NotBelow => ModLibraryWarnings.KeyVersionNotBelow,
            _ => null,
        };

    /// <summary>
    /// 补充说明的文案键（三角文案的第三句：登记情况 / 库里的状态）；不需要补充时为 null。
    /// </summary>
    public string? NoteKey => Kind switch
    {
        ModWarningKind.Unregistered => ModLibraryWarnings.KeyUnregistered,
        ModWarningKind.UnregisteredWorkshop => ModLibraryWarnings.KeyUnregisteredWorkshop,
        ModWarningKind.Status => ModLibraryWarnings.KeyStatus,
        _ => null,
    };

    /// <summary>版本支持说明这一句需要的参数个数（0 = 未声明，1 = 只有声明版本，2 = 声明版本 + 当前版本）。</summary>
    public int VersionArgumentCount => VersionSupport switch
    {
        ModVersionSupport.Below or ModVersionSupport.NotBelow => 2,
        ModVersionSupport.DeclaredUnknownGame => 1,
        _ => 0,
    };

    /// <summary>补充说明这一句需要的参数个数（库里状态那句要带上 status 原文）。</summary>
    public int NoteArgumentCount => Kind == ModWarningKind.Status ? 1 : 0;
}

/// <summary>
/// Mod 库行三角提示的唯一判定点。
/// <para>
/// 口径：三角只讲「这个 mod 声明支持哪个版本、和当前游戏版本对不对得上」，
/// 登记情况与 mods.status 作为补充说明，不做「mod 坏了 / 不兼容」这类判断。
/// 版本对照沿用 <see cref="ModVersion"/> 的 major.minor 口径：任意一边读不出两段数字就不下结论。
/// </para>
/// </summary>
public static class ModLibraryWarnings
{
    /// <summary>mods.status 判为正常时的取值。</summary>
    public const string ReadyStatus = "ready_to_play";

    // 下面这些键必须与 src/PdxLauncher.App/Strings/*/Resources.resw 的资源名逐字一致（Core 不引用 App 生成的
    // Keys 常量，所以写成字符串常量，由自检 ModLibraryWarningChecks 保证键名没写错）。
    /// <summary>三角的无障碍名。</summary>
    public const string NameKey = "ModlibWarningName";

    /// <summary>未声明支持版本。</summary>
    public const string KeyVersionUndeclared = "ModlibVersionSupportUndeclared";

    /// <summary>声明了版本但当前游戏版本未知。</summary>
    public const string KeyVersionDeclaredUnknownGame = "ModlibVersionSupportDeclaredUnknownGame";

    /// <summary>声明了版本，当前游戏版本也已知。</summary>
    public const string KeyVersionDeclared = "ModlibVersionSupportDeclared";

    /// <summary>声明的版本不低于当前游戏版本。</summary>
    public const string KeyVersionNotBelow = "ModlibVersionSupportNotBelow";

    /// <summary>声明的版本低于当前游戏版本。</summary>
    public const string KeyVersionBelow = "ModlibVersionSupportBelow";

    /// <summary>磁盘 mod 未在官方库登记。</summary>
    public const string KeyUnregistered = "ModlibWarningUnregistered";

    /// <summary>订阅 mod 未在官方库登记。</summary>
    public const string KeyUnregisteredWorkshop = "ModlibWarningUnregisteredWorkshop";

    /// <summary>mods.status 不是 ready_to_play（参数是原始 status 值）。</summary>
    public const string KeyStatus = "ModlibWarningStatus";

    /// <summary>
    /// 判定一行要不要挂三角，以及三段文案各取哪个键。
    /// 优先级保持与原先一致：未登记 → 状态 ≠ ready_to_play → 声明版本低于当前游戏版本。
    /// </summary>
    /// <param name="entry">Mod 库的一行。</param>
    /// <param name="gameVersion">当前游戏版本（"1.13.11 (Matcha)"）；未知时传 null 或空串。</param>
    public static ModWarningInfo Resolve(ModLibraryEntry entry, string? gameVersion)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var required = entry.RequiredVersion?.Trim() ?? string.Empty;
        var status = entry.Status?.Trim() ?? string.Empty;
        var game = gameVersion?.Trim() ?? string.Empty;
        var support = ClassifyVersionSupport(required, game);

        var kind = ModWarningKind.None;
        if (entry.IsUnregistered)
        {
            kind = SteamWorkshop.GetWorkshopId(entry.SteamId, entry.GameRegistryId) is null
                ? ModWarningKind.Unregistered
                : ModWarningKind.UnregisteredWorkshop;
        }
        else if (!string.Equals(status, ReadyStatus, StringComparison.OrdinalIgnoreCase))
        {
            kind = ModWarningKind.Status;
        }
        else if (support == ModVersionSupport.Below)
        {
            kind = ModWarningKind.VersionBelow;
        }

        return new ModWarningInfo(kind, support, required, game, status);
    }

    /// <summary>把「声明版本 vs 当前版本」归到四种对照结论之一。</summary>
    public static ModVersionSupport ClassifyVersionSupport(string? requiredVersion, string? gameVersion)
    {
        if (!ModVersion.TryReadMajorMinor(requiredVersion, out _, out _))
        {
            return ModVersionSupport.Undeclared;
        }

        if (!ModVersion.TryReadMajorMinor(gameVersion, out _, out _))
        {
            return ModVersionSupport.DeclaredUnknownGame;
        }

        return ModVersion.CompareMajorMinor(requiredVersion, gameVersion) < 0
            ? ModVersionSupport.Below
            : ModVersionSupport.NotBelow;
    }
}
