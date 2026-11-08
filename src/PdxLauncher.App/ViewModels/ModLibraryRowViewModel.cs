namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Steam;

/// <summary>
/// Mod 库里的一行：把 <see cref="ModLibraryEntry"/> 翻译成界面直接可绑的只读文案
/// （大小、订阅日期、播放集归属、警告文案）。
/// 本类不含任何 Microsoft.UI.Xaml 类型；只有缩略图路径是可观察的 ——
/// 联网补图是异步的，补到图后要就地刷新这一行，而不是整页重建。
/// </summary>
public sealed partial class ModLibraryRowViewModel : LocalizedObservableObject
{
    /// <summary>当前游戏版本（与 mod 声明的支持版本对照）；未知时为 null。</summary>
    private readonly string? _gameVersion;

    /// <summary>缩略图路径；没有缩略图时为 null。联网补全后由 <see cref="ModLibraryViewModel"/> 更新。</summary>
    [ObservableProperty]
    private string? _thumbnailPath;

    /// <summary>译名；没有启用翻译、翻译失败或原文已是目标语言时为 null。</summary>
    [ObservableProperty]
    private string? _translatedName;

    public ModLibraryRowViewModel(
        ModLibraryEntry entry,
        string? gameVersion = null,
        int thumbnailSize = ModThumbnailSizes.MediumPixels)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        ModId = entry.ModId;
        ThumbnailSize = thumbnailSize;
        Name = entry.Name;
        ThumbnailPath = entry.ThumbnailPath;
        IsInPlaySet = entry.IsInPlaySet;
        WorkshopId = SteamWorkshop.GetWorkshopId(entry.SteamId, entry.GameRegistryId);
        WorkshopRef = ModWorkshopRef.Resolve(entry.SteamId, entry.GameRegistryId, entry.Source);
        _gameVersion = gameVersion;
    }

    /// <summary>原始记录，需要更多字段时直接取。</summary>
    public ModLibraryEntry Entry { get; }

    /// <summary>mod 主键。</summary>
    public Guid ModId { get; }

    /// <summary>mod 名称。</summary>
    public string Name { get; }

    /// <summary>是否有可用缩略图（XAML 用它决定显示图片还是占位图标）。</summary>
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);

    /// <summary>是否有可显示的译名。</summary>
    public bool HasTranslatedName => !string.IsNullOrWhiteSpace(TranslatedName);

    /// <summary>缩略图边长（像素）：由设置里的「Mod 图片大小」三档（32 / 48 / 72）决定。</summary>
    public double ThumbnailSize { get; }

    /// <summary>大小文案：1010.6KB / 128.96MB；未知时为空串。</summary>
    public string SizeText => ModSizeText.Format(Entry.SizeBytes);

    /// <summary>订阅日期文案：已订阅 2025/08/04；未知时为空串。</summary>
    public string SubscribedText => Entry.SubscribedOn is { } subscribedOn
        ? T(Keys.ModlibSubscribedOn, subscribedOn.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
        : string.Empty;

    /// <summary>来源：本地 / 订阅。徽标与「来源」筛选都只看它。</summary>
    public ModOrigin Origin => Entry.Origin;

    /// <summary>来源徽标文案：本地 / 订阅。</summary>
    public string OriginText => T(ModOrigins.DescribeKey(Entry.Origin));

    /// <summary>是不是本地 mod（XAML 用不同底色区分）。</summary>
    public bool IsLocalOrigin => Entry.Origin == ModOrigin.Local;

    /// <summary>是不是订阅 mod。</summary>
    public bool IsSubscribedOrigin => Entry.Origin == ModOrigin.Subscribed;

    /// <summary>副标题：大小与订阅日期用「 · 」拼接，任一段为空则自动省略，不留孤立分隔符。</summary>
    public string SubtitleText => string.Join(
        " · ",
        new[] { SizeText, SubscribedText }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>是否被至少一个播放集引用。</summary>
    public bool IsInPlaySet { get; }

    /// <summary>播放集归属文案：在 N 个播放集中；不属于任何播放集时为空串（不显示）。</summary>
    /// <remarks>
    /// 不在任何播放集里是常态（387 个 mod 里绝大多数如此），原来那行「没有在播放集中」
    /// 每行都重复一遍纯属噪音：只保留有归属时的计数，没有归属就不显示。
    /// </remarks>
    public string PlaySetText => IsInPlaySet ? T(Keys.ModlibPlaySetCount, Entry.PlaySetIds.Count) : string.Empty;

    /// <summary>支持版本文案：支持版本 1.19.*；mod 没有声明版本时为空串（不显示）。</summary>
    public string SupportedVersionText => string.IsNullOrWhiteSpace(Entry.RequiredVersion)
        ? string.Empty
        : T(Keys.ModlibSupportedVersion, Entry.RequiredVersion);

    /// <summary>
    /// mod 作者自己写的版本（metadata.json 的 version / .mod 的 version），与支持的游戏版本不是一回事。
    /// 作者没写时为空串；Vic3 有相当一部分 mod 有这一项但没有 supported_game_version。
    /// </summary>
    public string ModVersionText => string.IsNullOrWhiteSpace(Entry.ModVersion)
        ? string.Empty
        : T(Keys.ModlibModVersion, Entry.ModVersion);

    /// <summary>两个版本都没声明时显示一句「未声明版本」，让「没数据」和「读取失败」可以区分。</summary>
    public string VersionUndeclaredText => string.IsNullOrWhiteSpace(Entry.ModVersion)
        && string.IsNullOrWhiteSpace(Entry.RequiredVersion)
            ? T(Keys.ModlibVersionUndeclared)
            : string.Empty;

    /// <summary>「未声明版本」的提示文案（同时作为 ToolTip）。</summary>
    public string VersionUndeclaredHint => T(Keys.ModlibVersionUndeclaredHint);

    /// <summary>Steam 创意工坊物品 id；本地 mod 拿不到时为 null。</summary>
    public string? WorkshopId { get; }

    /// <summary>是否有创意工坊页面（决定「在 Steam / 浏览器中打开」两个按钮是否可用）。</summary>
    public bool HasWorkshopPage => !string.IsNullOrWhiteSpace(WorkshopId);

    /// <summary>
    /// 这一行对应的工坊引用（平台 + 编号）：点「打开详情页」时用它建详情行。
    /// 永远非 null —— 解析不出编号时 <see cref="ModWorkshopRef.CanOpen"/> 为 false 并带原因文案键。
    /// </summary>
    public ModWorkshopRef WorkshopRef { get; }

    /// <summary>能否进创意工坊详情页（决定「打开详情页」按钮是否可用）。</summary>
    public bool HasWorkshopDetail => WorkshopRef.CanOpen;

    /// <summary>
    /// 「打开详情页」按钮的提示文案：可用时是动作名，不可用时换成说明为什么打不开
    /// —— 按钮置灰后没有别的途径告诉用户「这行是本地 mod」。
    /// </summary>
    public string WorkshopDetailText => WorkshopRef.CanOpen
        ? T(Keys.ModlibOpenDetail)
        : WorkshopRef.StatusTextKey is { Length: > 0 } statusKey ? T(statusKey) : T(Keys.ModlibOpenDetail);

    /// <summary>磁盘上有没有可打开的本地文件（目录型看 DirPath，压缩包型看 ArchivePath）。</summary>
    public bool HasLocalContent
        => !string.IsNullOrWhiteSpace(Entry.DirPath) || !string.IsNullOrWhiteSpace(Entry.ArchivePath);

    /// <summary>是否需要版本支持提示：登记情况、库里状态或声明的支持版本对不上。</summary>
    public bool HasWarning => WarningText.Length != 0;

    /// <summary>版本支持提示（同时作为 ToolTip 文案）；没有问题时为空串。</summary>
    public string WarningText => BuildWarning(Entry, _gameVersion);

    /// <summary>行的自动化 Id（每个 mod 唯一，供 UI 测试定位）。</summary>
    public string RowAutomationId => "modLibraryRow_" + ModId.ToString("N");

    /// <summary>
    /// Steam 侧状态（订阅 / 已安装 / 需要更新 / 下载中 / 本地禁用）。
    /// Steam 直连不可用、或这行不是工坊 mod 时为 null（不显示徽标）。
    /// </summary>
    public WorkshopInstallState? SteamState => Entry.SteamState;

    /// <summary>是否要显示 Steam 状态徽标。</summary>
    public bool ShowSteamStateBadge => SteamState is not null && WorkshopStateText.Resolve(SteamState).IsVisible;

    /// <summary>Steam 状态徽标文案（下载中会带上百分比）。没有可显示的状态时为空串。</summary>
    public string SteamStateBadgeText
    {
        get
        {
            var info = WorkshopStateText.Resolve(SteamState);
            return info.Badge switch
            {
                WorkshopStateBadge.None => string.Empty,
                WorkshopStateBadge.Downloading when info.Percent is { } percent => T(info.Key, percent),
                WorkshopStateBadge.Downloading => T(WorkshopStateText.KeyDownloadingNoPercent),
                _ => T(info.Key),
            };
        }
    }

    /// <summary>是否可以在 Steam 里触发这条的下载（工坊 mod + 没在下载 + 已经订阅）。</summary>
    public bool CanTriggerSteamDownload =>
        HasWorkshopPage
        && SteamState is { IsSubscribed: not false, IsInstalled: not true, IsDownloading: not true };

    /// <summary>这条是否在 Steam 里被设为「本地禁用」（订阅还在，但游戏不加载）。</summary>
    public bool IsSteamDisabledLocally => SteamState?.IsDisabledLocally == true;

    /// <summary>「在 Steam 里禁用 / 启用」按钮的文案：当前禁用时是「启用」，否则是「禁用」。</summary>
    public string SteamDisableActionText => T(IsSteamDisabledLocally ? Keys.ModlibSteamEnableName : Keys.ModlibSteamDisableName);

    /// <summary>支持版本文本的自动化 Id。</summary>
    public string VersionAutomationId => "modLibraryVersion_" + ModId.ToString("N");

    /// <summary>mod 版本（作者版本）文本的自动化 Id。</summary>
    public string ModVersionAutomationId => "modLibraryModVersion_" + ModId.ToString("N");

    /// <summary>行尾「打开本地文件夹」按钮的自动化 Id。</summary>
    public string OpenFolderAutomationId => "modLibraryOpenFolder_" + ModId.ToString("N");

    /// <summary>行尾「在 Steam 中打开」按钮的自动化 Id。</summary>
    public string OpenSteamAutomationId => "modLibraryOpenSteam_" + ModId.ToString("N");

    /// <summary>行尾「在浏览器中打开」按钮的自动化 Id。</summary>
    public string OpenBrowserAutomationId => "modLibraryOpenBrowser_" + ModId.ToString("N");

    /// <summary>行尾「打开详情页」按钮的自动化 Id。</summary>
    public string WorkshopDetailAutomationId => "modLibraryOpenDetail_" + ModId.ToString("N");

    /// <summary>行尾「在 Steam 里触发下载」按钮的自动化 Id。</summary>
    public string SteamDownloadAutomationId => "modLibrarySteamDownload_" + ModId.ToString("N");

    /// <summary>行尾「在 Steam 里禁用 / 启用」按钮的自动化 Id。</summary>
    public string SteamDisableAutomationId => "modLibrarySteamDisable_" + ModId.ToString("N");

    /// <summary>Steam 状态徽标的自动化 Id。</summary>
    public string SteamStateAutomationId => "modLibrarySteamState_" + ModId.ToString("N");

    partial void OnThumbnailPathChanged(string? value) => OnPropertyChanged(nameof(HasThumbnail));

    partial void OnTranslatedNameChanged(string? value) => OnPropertyChanged(nameof(HasTranslatedName));

    /// <summary>
    /// 版本支持提示：三角只讲「这个 mod 声明支持哪个版本、和当前游戏版本对不对得上」，
    /// 登记情况与库里状态作为补充说明跟在后面，不写「异常 / 不兼容」这类判断。
    /// 判定在 <see cref="ModLibraryWarnings"/>（Core，可被自检覆盖），优先级仍是
    /// 「磁盘未登记 → 库里状态 ≠ ready_to_play → 声明版本低于当前游戏版本」。
    /// 没有问题时返回空串。
    /// </summary>
    /// <param name="entry">mod 行数据。</param>
    /// <param name="gameVersion">当前游戏版本（用于对照 requiredVersion）；未知时传 null。</param>
    private static string BuildWarning(ModLibraryEntry entry, string? gameVersion)
    {
        var info = ModLibraryWarnings.Resolve(entry, gameVersion);
        if (!info.HasTriangle)
        {
            return string.Empty;
        }

        var parts = new List<string>(3);
        if (info.VersionKey is { } versionKey)
        {
            parts.Add(info.VersionArgumentCount switch
            {
                2 => T(versionKey, info.RequiredVersion, info.GameVersion),
                1 => T(versionKey, info.RequiredVersion),
                _ => T(versionKey),
            });
        }

        if (info.ConclusionKey is { } conclusionKey)
        {
            parts.Add(T(conclusionKey));
        }

        if (info.NoteKey is { } noteKey)
        {
            parts.Add(info.NoteArgumentCount == 1 ? T(noteKey, info.Status) : T(noteKey));
        }

        return parts.Count == 0 ? string.Empty : string.Join(' ', parts);
    }
}
