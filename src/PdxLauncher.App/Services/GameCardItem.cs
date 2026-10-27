namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 游戏库卡片的纯数据投影：只含字符串与 Core 契约，不含任何 UI 类型，
/// 因此可以安全地放在 ViewModel 里，由 XAML 侧把路径转成图片。
/// </summary>
public sealed record GameCardItem(
    GameInstallation Game,
    string DisplayName,
    string VersionText,
    string? CoverImagePath,
    string? LogoImagePath,
    GameSupport Support)
{
    /// <summary>
    /// 卡片模板里的本地化入口：<c>DataTemplate</c> 的 <c>x:DataType</c> 是本记录，拿不到页面上的 <c>L</c>，
    /// 所以自己暴露一份，XAML 里写 <c>{x:Bind L.LibraryLaunch, Mode=OneWay}</c>。
    /// </summary>
    public Localizer L => Localizer.Instance;

    /// <summary>卡片右上角角标文案；完整适配不显示角标。文案在 getter 里按当前语言取。</summary>
    public string? SupportBadge => Support switch
    {
        GameSupport.Unsupported => L.LibraryBadgeUnsupported,
        GameSupport.LaunchOnly => L.LibraryBadgeLaunchOnly,
        _ => null,
    };

    /// <summary>是否显示角标。</summary>
    public bool HasSupportBadge => !string.IsNullOrEmpty(SupportBadge);

    /// <summary>能识别并展示（不代表能管理内容）。</summary>
    public bool IsSupported => Support != GameSupport.Unsupported;

    /// <summary>卡片封面图的 AutomationId；按游戏 id 保证同一页内唯一。</summary>
    public string CoverAutomationId => "imgCover_" + Game.GameId;

    /// <summary>卡片"启动"按钮的 AutomationId；按游戏 id 保证同一页内唯一。</summary>
    public string LaunchAutomationId => "btnLaunch_" + Game.GameId;

    /// <summary>卡片游戏名的 AutomationId；按游戏 id 保证同一页内唯一。</summary>
    public string NameAutomationId => "txtCardName_" + Game.GameId;

    /// <summary>卡片版本号的 AutomationId；按游戏 id 保证同一页内唯一。</summary>
    public string VersionAutomationId => "txtCardVersion_" + Game.GameId;

    /// <summary>
    /// 卡片条目的显示名。GridViewItem 会拿数据项的 ToString 当无障碍名称，
    /// record 的默认实现会把整条安装信息拼进去，这里改成只返回游戏名。
    /// </summary>
    public override string ToString() => DisplayName;
}
