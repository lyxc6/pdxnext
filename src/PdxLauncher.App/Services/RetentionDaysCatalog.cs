namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;

/// <summary>
/// 设置页里「保留 N 天」类下拉框的一项（Mod 缩略图、DLC 简介共用）。
/// 标题统一按 <c>common.daysCount</c> 拼，说明文字由目录各自提供键。
/// </summary>
public sealed class RetentionDaysOption : LocalizedObservableObject
{
    private readonly string _descriptionKey;

    internal RetentionDaysOption(int days, string descriptionKey)
    {
        Days = days;
        _descriptionKey = descriptionKey;
    }

    /// <summary>写进 settings.json 的天数。</summary>
    public int Days { get; }

    /// <summary>下拉里显示的名字，例如「30 天」。</summary>
    public string Title => T(Keys.CommonDaysCount, Days);

    /// <summary>下拉项的 UI 自动化名称取自 ToString，返回本地化标题。</summary>
    public override string ToString() => Title;

    /// <summary>选中后显示在下方的一行说明。</summary>
    public string Description => T(_descriptionKey);
}
