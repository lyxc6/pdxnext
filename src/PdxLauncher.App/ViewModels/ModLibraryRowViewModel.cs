namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// Mod 库里的一行：把 <see cref="ModLibraryEntry"/> 翻译成界面直接可绑的只读文案
/// （大小、订阅日期、播放集归属、警告文案）。
/// 本类不含任何 Microsoft.UI.Xaml 类型；只有缩略图路径是可观察的 ——
/// 联网补图是异步的，补到图后要就地刷新这一行，而不是整页重建。
/// </summary>
public sealed partial class ModLibraryRowViewModel : LocalizedObservableObject
{
    /// <summary>mods.status 判为正常时的取值。</summary>
    private const string ReadyStatus = "ready_to_play";

    /// <summary>当前游戏版本（用于判断版本是否可能过旧）；未知时为 null。</summary>
    private readonly string? _gameVersion;

    /// <summary>缩略图路径；没有缩略图时为 null。联网补全后由 <see cref="ModLibraryViewModel"/> 更新。</summary>
    [ObservableProperty]
    private string? _thumbnailPath;

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

    /// <summary>缩略图边长（像素）：由设置里的「Mod 图片大小」三档（32 / 48 / 72）决定。</summary>
    public double ThumbnailSize { get; }

    /// <summary>大小文案：1010.6KB / 128.96MB；未知时为空串。</summary>
    public string SizeText => ModSizeText.Format(Entry.SizeBytes);

    /// <summary>订阅日期文案：已订阅 2025/08/04；未知时为空串。</summary>
    public string SubscribedText => Entry.SubscribedOn is { } subscribedOn
        ? T(Keys.ModlibSubscribedOn, subscribedOn.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
        : string.Empty;

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

    /// <summary>是否需要警示图标：磁盘未登记、状态异常或版本可能不兼容。</summary>
    public bool HasWarning => WarningText.Length != 0;

    /// <summary>警示说明（同时作为 ToolTip 文案）；无警示时为空串。</summary>
    public string WarningText => BuildWarning(Entry, _gameVersion);

    /// <summary>行的自动化 Id（每个 mod 唯一，供 UI 测试定位）。</summary>
    public string RowAutomationId => "modLibraryRow_" + ModId.ToString("N");

    /// <summary>支持版本文本的自动化 Id。</summary>
    public string VersionAutomationId => "modLibraryVersion_" + ModId.ToString("N");

    /// <summary>行尾「打开本地文件夹」按钮的自动化 Id。</summary>
    public string OpenFolderAutomationId => "modLibraryOpenFolder_" + ModId.ToString("N");

    /// <summary>行尾「在 Steam 中打开」按钮的自动化 Id。</summary>
    public string OpenSteamAutomationId => "modLibraryOpenSteam_" + ModId.ToString("N");

    /// <summary>行尾「在浏览器中打开」按钮的自动化 Id。</summary>
    public string OpenBrowserAutomationId => "modLibraryOpenBrowser_" + ModId.ToString("N");

    /// <summary>行尾「打开详情页」按钮的自动化 Id。</summary>
    public string WorkshopDetailAutomationId => "modLibraryOpenDetail_" + ModId.ToString("N");

    partial void OnThumbnailPathChanged(string? value) => OnPropertyChanged(nameof(HasThumbnail));

    /// <summary>
    /// 警示文案：优先级为「磁盘未登记 → 状态异常 → 版本可能过旧」，对应原版启动器同一个三角图标。
    /// 没有问题时返回空串。
    /// </summary>
    /// <param name="entry">mod 行数据。</param>
    /// <param name="gameVersion">当前游戏版本（用于比较 requiredVersion）；未知时传 null。</param>
    private static string BuildWarning(ModLibraryEntry entry, string? gameVersion)
    {
        if (entry.IsUnregistered)
        {
            return T(Keys.ModlibWarningUnregistered);
        }

        if (!string.Equals(entry.Status, ReadyStatus, StringComparison.OrdinalIgnoreCase))
        {
            return T(Keys.ModlibWarningStatus, entry.Status);
        }

        return ModVersion.IsOutdated(entry.RequiredVersion, gameVersion)
            ? T(Keys.ModlibWarningOutdated, entry.RequiredVersion, gameVersion)
            : string.Empty;
    }
}
