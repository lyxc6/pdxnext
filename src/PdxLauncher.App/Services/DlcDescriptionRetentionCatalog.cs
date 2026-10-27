namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;

/// <summary>
/// DLC 简介缓存保留天数的可选项目录（取值真源是
/// <see cref="PdxLauncher.Core.Configuration.AppSettings.DlcDescriptionCacheDays"/>）。
/// 与 <see cref="ModThumbnailRetentionCatalog"/> 共用 <see cref="RetentionDaysOption"/>。
/// </summary>
public static class DlcDescriptionRetentionCatalog
{
    /// <summary>新建配置的默认保留天数。</summary>
    public const int DefaultDays = 7;

    public static IReadOnlyList<RetentionDaysOption> All { get; } = new[]
    {
        new RetentionDaysOption(1, Keys.SettingsDataDlcDays1Desc),
        new RetentionDaysOption(3, Keys.SettingsDataDlcDays3Desc),
        new RetentionDaysOption(7, Keys.SettingsDataDlcDays7Desc),
        new RetentionDaysOption(14, Keys.SettingsDataDlcDays14Desc),
        new RetentionDaysOption(30, Keys.SettingsDataDlcDays30Desc),
    };

    /// <summary>按天数找到目录中的同一实例；未知值回退默认项（7 天）。</summary>
    public static RetentionDaysOption Resolve(int days)
    {
        foreach (var option in All)
        {
            if (option.Days == days)
            {
                return option;
            }
        }

        return All.First(static option => option.Days == DefaultDays);
    }
}
