namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 设置页「翻译引擎」下拉的一项。文案按 i18n 键取，语言切换后由基类重新发 PropertyChanged。
/// </summary>
public sealed class TranslationEngineOption : LocalizedObservableObject
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    internal TranslationEngineOption(string key, string titleKey, string descriptionKey)
    {
        Key = key;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
    }

    /// <summary>写进 settings.json 的值，见 <see cref="TranslationEngineModes"/>。</summary>
    public string Key { get; }

    /// <summary>下拉里显示的名字。</summary>
    public string Title => T(_titleKey);

    /// <summary>下拉项的 UI 自动化名称取自 ToString，返回本地化标题。</summary>
    public override string ToString() => Title;

    /// <summary>选中后显示在下方的一行说明。</summary>
    public string Description => T(_descriptionKey);
}

/// <summary>
/// 翻译引擎模式的目录：自动（自填 API → 微软 → 谷歌云 → 内置免费，逐级回落）/
/// 仅自填 API / 仅内置免费服务 / 仅微软翻译 / 仅谷歌云翻译。
/// 与 <see cref="ProxyModeCatalog"/> 一样返回同一批实例，
/// 这样下拉控件的 SelectedItem 能靠引用相等匹配上。
/// </summary>
public static class TranslationEngineCatalog
{
    /// <summary>全部可选模式，顺序即下拉框顺序。</summary>
    public static readonly IReadOnlyList<TranslationEngineOption> All = new[]
    {
        new TranslationEngineOption(
            TranslationEngineModes.Auto,
            Keys.SettingsTranslateEngineAutoTitle,
            Keys.SettingsTranslateEngineAutoDesc),
        new TranslationEngineOption(
            TranslationEngineModes.Custom,
            Keys.SettingsTranslateEngineCustomTitle,
            Keys.SettingsTranslateEngineCustomDesc),
        new TranslationEngineOption(
            TranslationEngineModes.Builtin,
            Keys.SettingsTranslateEngineBuiltinTitle,
            Keys.SettingsTranslateEngineBuiltinDesc),
        new TranslationEngineOption(
            TranslationEngineModes.Microsoft,
            Keys.SettingsTranslateEngineMicrosoftTitle,
            Keys.SettingsTranslateEngineMicrosoftDesc),
        new TranslationEngineOption(
            TranslationEngineModes.GoogleCloud,
            Keys.SettingsTranslateEngineGoogleCloudTitle,
            Keys.SettingsTranslateEngineGoogleCloudDesc),
    };

    /// <summary>按 settings.json 里的值解析出对应项；未知值回落到「自动」。</summary>
    public static TranslationEngineOption Resolve(string? mode)
    {
        var normalized = TranslationEngineModes.Normalize(mode);
        foreach (var option in All)
        {
            if (string.Equals(option.Key, normalized, StringComparison.Ordinal))
            {
                return option;
            }
        }

        return All[0];
    }
}
