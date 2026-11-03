namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;

/// <summary>
/// 设置页里「保留 N 天」类下拉框的一项（Mod 缩略图、DLC 简介共用）。
/// 标题统一按 <c>common.daysCount</c> 拼，说明文字由所属目录提供键。
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

/// <summary>
/// 「保留 N 天」下拉的可选项目录：天数表、各自的说明键与默认项。
/// Resolve 返回目录里的同一实例，下拉控件的 SelectedItem 才能按引用相等匹配。
/// 两个目录（Mod 缩略图 / DLC 简介）只是天数与描述键不同，逻辑共用这一份。
/// </summary>
public sealed class RetentionDaysCatalog
{
    /// <summary>Mod 缩略图缓存：7 / 30 / 90 / 365 天，默认 30 天。</summary>
    public static RetentionDaysCatalog ModThumbnails { get; } = new(
        30,
        new[]
        {
            new RetentionDaysOption(7, Keys.SettingsModThumbnailsDays7Desc),
            new RetentionDaysOption(30, Keys.SettingsModThumbnailsDays30Desc),
            new RetentionDaysOption(90, Keys.SettingsModThumbnailsDays90Desc),
            new RetentionDaysOption(365, Keys.SettingsModThumbnailsDays365Desc),
        });

    /// <summary>DLC 简介缓存：1 / 3 / 7 / 14 / 30 天，默认 7 天。</summary>
    public static RetentionDaysCatalog DlcDescriptions { get; } = new(
        7,
        new[]
        {
            new RetentionDaysOption(1, Keys.SettingsDataDlcDays1Desc),
            new RetentionDaysOption(3, Keys.SettingsDataDlcDays3Desc),
            new RetentionDaysOption(7, Keys.SettingsDataDlcDays7Desc),
            new RetentionDaysOption(14, Keys.SettingsDataDlcDays14Desc),
            new RetentionDaysOption(30, Keys.SettingsDataDlcDays30Desc),
        });

    private RetentionDaysCatalog(int defaultDays, RetentionDaysOption[] all)
    {
        All = all;
        DefaultDays = defaultDays;
        Default = all.First(option => option.Days == defaultDays);
    }

    /// <summary>新建配置的默认保留天数，也是未知值的回退天数。</summary>
    public int DefaultDays { get; }

    /// <summary>默认项；Resolve 遇到未知天数时回退到这里。</summary>
    public RetentionDaysOption Default { get; }

    /// <summary>下拉框的 ItemsSource。</summary>
    public IReadOnlyList<RetentionDaysOption> All { get; }

    /// <summary>按天数找到目录中的同一实例；未知值回退默认项。</summary>
    public RetentionDaysOption Resolve(int days) =>
        All.FirstOrDefault(option => option.Days == days) ?? Default;
}
