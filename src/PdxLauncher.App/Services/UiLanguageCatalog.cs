namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 设置页「界面语言」下拉框的一项。<see cref="Key"/> 为 null 表示跟随系统
/// （与 <see cref="AppSettings.UiLanguage"/> 的 null 语义一致）。
/// </summary>
public sealed class UiLanguageOption : LocalizedObservableObject
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    internal UiLanguageOption(string? key, string titleKey, string descriptionKey)
    {
        Key = key;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
    }

    /// <summary>写进 settings.json 的值：<c>zh-CN</c> / <c>en-US</c>，null 表示跟随系统。</summary>
    public string? Key { get; }

    public string Title => T(_titleKey);

    /// <summary>下拉项的 UI 自动化名称取自 ToString，返回本地化标题。</summary>
    public override string ToString() => Title;

    public string Description => T(_descriptionKey);
}

/// <summary>界面语言的可选项目录；Resolve 返回目录中的同一实例，ComboBox 才能按引用相等匹配。</summary>
public static class UiLanguageCatalog
{
    public static IReadOnlyList<UiLanguageOption> All { get; } = new[]
    {
        new UiLanguageOption(
            null,
            Keys.SettingsInterfaceLanguageSystemTitle,
            Keys.SettingsInterfaceLanguageSystemDesc),
        new UiLanguageOption(
            UiLanguages.SimplifiedChinese,
            Keys.SettingsInterfaceLanguageZhCNTitle,
            Keys.SettingsInterfaceLanguageZhCNDesc),
        new UiLanguageOption(
            UiLanguages.English,
            Keys.SettingsInterfaceLanguageEnUSTitle,
            Keys.SettingsInterfaceLanguageEnUSDesc),
    };

    /// <summary>按 settings.json 里的值找同一实例；null / 未知值回退「跟随系统」。</summary>
    public static UiLanguageOption Resolve(string? uiLanguage)
    {
        if (string.IsNullOrWhiteSpace(uiLanguage))
        {
            return All[0];
        }

        foreach (var option in All)
        {
            if (option.Key is not null
                && string.Equals(option.Key, uiLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        return All[0];
    }
}
