namespace PdxLauncher.Core.Mcp;

using PdxLauncher.Core.Configuration;

/// <summary>
/// 设置页 / 播放集页字段的「展示值」归一化。
/// <para>
/// 快照、变更计划的 <c>expectedHash</c> 与 App 执行器必须用同一套文本，
/// 否则「AI 读到 A → 提议改成 B → App 比较 A' ≠ A → 永远批不过」。
/// 可空设置项在这里补默认语义（null 表示跟随系统 / 默认布局），不要直接 <c>ToString()</c>。
/// </para>
/// </summary>
public static class McpPageValueText
{
    /// <summary>布尔统一小写，与播放集 <c>isActive</c> 的写法一致。</summary>
    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>设置项的展示值；敏感字段不在此白名单内，调用方只传白名单键。</summary>
    public static string FromSetting(AppSettings settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return key switch
        {
            nameof(AppSettings.UiLanguage) => Language(settings.UiLanguage, "follow-system"),
            nameof(AppSettings.TranslateTargetLanguage) => Language(settings.TranslateTargetLanguage, "follow-ui"),
            nameof(AppSettings.BackgroundDisplayMode) => BackgroundDisplayModes.Normalize(settings.BackgroundDisplayMode),
            nameof(AppSettings.ModThumbnailSize) => ModThumbnailSizes.Normalize(settings.ModThumbnailSize),
            nameof(AppSettings.PlaySetPageDisplayMode) => PlaySetPageDisplayModes.Normalize(settings.PlaySetPageDisplayMode),
            nameof(AppSettings.AlwaysOnTop) => Bool(settings.AlwaysOnTop),
            nameof(AppSettings.AutoCheckUpdates) => Bool(settings.AutoCheckUpdates),
            nameof(AppSettings.TranslateEnabled) => Bool(settings.TranslateEnabled),
            nameof(AppSettings.TranslateNamesEnabled) => Bool(settings.TranslateNamesEnabled),
            nameof(AppSettings.TranslateDescriptionEnabled) => Bool(settings.TranslateDescriptionEnabled),
            nameof(AppSettings.TranslateChangelogEnabled) => Bool(settings.TranslateChangelogEnabled),
            nameof(AppSettings.TranslateCommentsEnabled) => Bool(settings.TranslateCommentsEnabled),
            nameof(AppSettings.WorkshopPageSize) => settings.WorkshopPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
    }

    /// <summary>null / 空白表示「跟随系统」或「跟随界面语言」，各自用不同哨兵值，避免回写时混淆。</summary>
    public static string Language(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : UiLanguages.Normalize(value);
}
