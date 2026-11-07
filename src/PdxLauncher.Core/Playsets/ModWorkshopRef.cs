namespace PdxLauncher.Core.Playsets;

using PdxLauncher.Core.Online;

/// <summary>
/// Mod 库的一行能不能进创意工坊详情页，以及进哪个平台。
/// <para>
/// Mod 库只有 (mods.steamId, gameRegistryId, mods.source) 三串文本，没有列表接口那种对象；
/// 这里把它们翻译成详情页认识的 (平台, 编号)。编号解析复用 <see cref="SteamWorkshop"/> 的
/// 「只认 ASCII 数字」判定，避免拼出一个打不开的地址。
/// </para>
/// </summary>
/// <param name="Source">工坊平台；解析不出可用编号时不影响取值，只看 <see cref="CanOpen"/>。</param>
/// <param name="Id">平台内的物品编号；不可用时为空串。</param>
/// <param name="StatusTextKey">不可用时的原因文案键（界面直接 Localizer.T 这个键）；可用时为 null。</param>
public sealed record ModWorkshopRef(WorkshopSource Source, string Id, string? StatusTextKey)
{
    /// <summary>编号可用，能进详情页。</summary>
    public bool CanOpen => Id.Length > 0;

    /// <summary>本地 mod：mods.source = local，或磁盘扫到但库里没登记。</summary>
    public const string SourceLocal = "local";

    /// <summary>mods.source 里表示 Paradox 渠道的写法（老游戏也可能写成空串）。</summary>
    public const string SourceParadox = "paradox";

    /// <summary>mods.source 里表示 Steam 渠道的写法。</summary>
    public const string SourceSteam = "steam";

    /// <summary>CS2 的 pdx-mods 目录登记写法，见 PlaysetConfigDocument.ModSourcePdxMods。</summary>
    public const string SourcePdxMods = "pdx_mods";

    /// <summary>CS2 注册 ID 的 pdx 前缀：GameRegistryId = "pdx:&lt;sourceId&gt;"。</summary>
    internal const string PdxRegistryPrefix = "pdx:";

    // 下面四个键必须与 src/PdxLauncher.App/Strings/*/Resources.resw 的资源名逐字一致（Core 不引用 App 生成的
    // Keys 常量，所以在这里写成字符串常量，由自检 ModWorkshopRefChecks 保证键名没写错）。
    /// <summary>这行根本没有工坊编号（本地 mod）。</summary>
    public const string StatusKeyNoWorkshop = "ModlibDetailNoWorkshop";

    /// <summary>Steam 渠道，但注册 ID 里没有 ugc_ 编号。</summary>
    public const string StatusKeySteamUnresolved = "ModlibDetailSteamUnresolved";

    /// <summary>Paradox 渠道，但 pdx: 后面的编号不是纯数字。</summary>
    public const string StatusKeyPdxUnresolved = "ModlibDetailPdxUnresolved";

    /// <summary>来源字段认不出来，不猜平台。</summary>
    public const string StatusKeyUnknownSource = "ModlibDetailUnknownSource";

    /// <summary>
    /// 解析一行 Mod 库记录。
    /// </summary>
    /// <param name="steamId">mods.steamId，可能为 null / 带空白。</param>
    /// <param name="gameRegistryId">gameRegistryId，可能为空串。</param>
    /// <param name="source">mods.source 或 pdx-mods 的登记值，可能为 null / 空串。</param>
    public static ModWorkshopRef Resolve(string? steamId, string? gameRegistryId, string? source)
    {
        // 1) Steam：先看 steamId，再回退注册 ID 里的 ugc_<id>（与「在 Steam 中打开」用同一套判定）。
        var workshopId = SteamWorkshop.GetWorkshopId(steamId, gameRegistryId);
        if (workshopId is not null)
        {
            return new ModWorkshopRef(WorkshopSource.Steam, workshopId, null);
        }

        var normalizedSource = source?.Trim();

        // 2) CS2 的 pdx-mods：注册 ID 形如 pdx:<sourceId>，编号必须是纯数字（Paradox 的 mod id 是数字）。
        if (string.Equals(normalizedSource, SourcePdxMods, StringComparison.OrdinalIgnoreCase))
        {
            return ResolvePdx(gameRegistryId);
        }

        // 3) 明确的本地 mod：没有任何工坊页可去。
        if (string.Equals(normalizedSource, SourceLocal, StringComparison.OrdinalIgnoreCase))
        {
            return new ModWorkshopRef(WorkshopSource.None, string.Empty, StatusKeyNoWorkshop);
        }

        // 4) 声明是 Steam 渠道却解析不出编号：说明是手工/本地放进 mods 表的行。
        if (string.Equals(normalizedSource, SourceSteam, StringComparison.OrdinalIgnoreCase))
        {
            return new ModWorkshopRef(WorkshopSource.Steam, string.Empty, StatusKeySteamUnresolved);
        }

        // 5) 声明是 Paradox 渠道：老游戏的注册 ID 有可能直接就是数字 mod id。
        if (string.Equals(normalizedSource, SourceParadox, StringComparison.OrdinalIgnoreCase))
        {
            return ResolvePdx(gameRegistryId);
        }

        // 6) 来源缺失或不认识：不猜平台，免得把本地目录名当成 Paradox mod id。
        return new ModWorkshopRef(WorkshopSource.None, string.Empty, StatusKeyUnknownSource);
    }

    /// <summary>从注册 ID 里取 pdx 编号；没有前缀时把整串当编号（老格式）。</summary>
    private static ModWorkshopRef ResolvePdx(string? gameRegistryId)
    {
        var candidate = gameRegistryId?.Trim() ?? string.Empty;
        if (candidate.StartsWith(PdxRegistryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[PdxRegistryPrefix.Length..];
        }

        // 不用 long.TryParse：它会接受 "+12"、"1_2"、"12L" 这些拼进 URL 就打不开的写法。
        if (!IsAllAsciiDigits(candidate))
        {
            return new ModWorkshopRef(WorkshopSource.Pdx, string.Empty, StatusKeyPdxUnresolved);
        }

        return new ModWorkshopRef(WorkshopSource.Pdx, candidate, null);
    }

    private static bool IsAllAsciiDigits(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
