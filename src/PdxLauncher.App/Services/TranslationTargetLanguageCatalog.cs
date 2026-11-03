namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 设置页「译文语言」下拉的一项。Key 为 null 表示「跟随界面语言」
/// （与 <see cref="AppSettings.TranslateTargetLanguage"/> 的 null 语义一致）。
/// </summary>
public sealed class TranslationTargetLanguageOption : LocalizedObservableObject
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    internal TranslationTargetLanguageOption(string? key, string titleKey, string descriptionKey)
    {
        Key = key;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
    }

    /// <summary>写进 settings.json 的语言键；null 表示跟随界面语言。</summary>
    public string? Key { get; }

    /// <summary>下拉里显示的名字。</summary>
    public string Title => T(_titleKey);

    /// <summary>下拉项的 UI 自动化名称取自 ToString，返回本地化标题。</summary>
    public override string ToString() => Title;

    /// <summary>选中后显示在下方的一行说明。</summary>
    public string Description => T(_descriptionKey);
}

/// <summary>
/// 译文语言的可选项目录：跟随界面语言 / 简体中文 / English。
/// Resolve 返回目录中的同一实例，下拉控件才能按引用相等匹配。
/// </summary>
public static class TranslationTargetLanguageCatalog
{
    /// <summary>全部可选项，顺序即下拉框顺序。</summary>
    public static readonly IReadOnlyList<TranslationTargetLanguageOption> All = new[]
    {
        new TranslationTargetLanguageOption(
            null,
            Keys.SettingsTranslateLanguageFollowTitle,
            Keys.SettingsTranslateLanguageFollowDesc),
        new TranslationTargetLanguageOption(
            UiLanguages.SimplifiedChinese,
            Keys.SettingsTranslateLanguageZhCNTitle,
            Keys.SettingsTranslateLanguageZhCNDesc),
        new TranslationTargetLanguageOption(
            UiLanguages.English,
            Keys.SettingsTranslateLanguageEnUSTitle,
            Keys.SettingsTranslateLanguageEnUSDesc),
    };

    /// <summary>按 settings.json 里的值找同一实例；null / 未知值回退「跟随界面语言」。</summary>
    public static TranslationTargetLanguageOption Resolve(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return All[0];
        }

        foreach (var option in All)
        {
            if (option.Key is not null
                && string.Equals(option.Key, language.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        return All[0];
    }
}
