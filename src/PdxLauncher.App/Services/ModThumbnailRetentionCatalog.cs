namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;

/// <summary>
/// Mod 缩略图缓存保留天数的可选项目录。
/// 取值真源是 <see cref="PdxLauncher.Core.Configuration.AppSettings.ModThumbnailCacheDays"/>；
/// Resolve 返回目录中的同一实例，ComboBox 的 SelectedItem 才能按引用相等匹配。
/// </summary>
public static class ModThumbnailRetentionCatalog
{
    /// <summary>新建配置的默认保留天数。</summary>
    public const int DefaultDays = 30;

    public static IReadOnlyList<RetentionDaysOption> All { get; } = new[]
    {
        new RetentionDaysOption(7, Keys.SettingsModThumbnailsDays7Desc),
        new RetentionDaysOption(30, Keys.SettingsModThumbnailsDays30Desc),
        new RetentionDaysOption(90, Keys.SettingsModThumbnailsDays90Desc),
        new RetentionDaysOption(365, Keys.SettingsModThumbnailsDays365Desc),
    };

    /// <summary>按天数找到目录中的同一实例；未知值回退默认项（30 天）。</summary>
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
