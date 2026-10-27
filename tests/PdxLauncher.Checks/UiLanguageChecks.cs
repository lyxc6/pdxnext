namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;

/// <summary>界面语言归一化与「界面语言 → Steam 商店语言」映射的自检。</summary>
public static class UiLanguageChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        // --- UiLanguages 归一化
        ChecksContext.Equal("语言: zh-CN 原样保留", UiLanguages.SimplifiedChinese, UiLanguages.Normalize(UiLanguages.SimplifiedChinese));
        ChecksContext.Equal("语言: en-US 原样保留", UiLanguages.English, UiLanguages.Normalize(UiLanguages.English));
        ChecksContext.Equal("语言: 大小写无关", UiLanguages.English, UiLanguages.Normalize(" EN-us "));
        ChecksContext.Equal("语言: zh-Hans-CN 折到简中", UiLanguages.SimplifiedChinese, UiLanguages.Normalize("zh-Hans-CN"));
        ChecksContext.Equal("语言: 未知值回退默认语言", UiLanguages.Default, UiLanguages.Normalize("fr-FR"));
        ChecksContext.Check(
            "语言: null 跟随系统且结果受支持",
            UiLanguages.IsSupported(UiLanguages.Normalize(null)),
            UiLanguages.Normalize(null));
        ChecksContext.Check("语言: 空白视为跟随系统", UiLanguages.IsFollowSystem("   ") && UiLanguages.IsFollowSystem(null));
        ChecksContext.Check("语言: 受支持集合只有两档", UiLanguages.All.Count == 2);

        // --- 系统 UI 语言折档
        ChecksContext.Equal("系统语言: zh-Hant-TW → 简中", UiLanguages.SimplifiedChinese, UiLanguages.ResolveFromCulture("zh-Hant-TW"));
        ChecksContext.Equal("系统语言: en-GB → 英文", UiLanguages.English, UiLanguages.ResolveFromCulture("en-GB"));
        ChecksContext.Equal("系统语言: fr-FR → 英文", UiLanguages.English, UiLanguages.ResolveFromCulture("fr-FR"));
        ChecksContext.Equal("系统语言: null → 默认语言", UiLanguages.Default, UiLanguages.ResolveFromCulture(null));

        // --- 界面语言 → Steam 商店 l= 参数
        ChecksContext.Equal("商店语言: 简中 → schinese", SteamStoreLanguage.SimplifiedChinese, SteamStoreLanguage.FromUiLanguage(UiLanguages.SimplifiedChinese));
        ChecksContext.Equal("商店语言: 英文 → english", SteamStoreLanguage.English, SteamStoreLanguage.FromUiLanguage(UiLanguages.English));
        ChecksContext.Equal("商店语言: null → schinese", SteamStoreLanguage.Default, SteamStoreLanguage.FromUiLanguage(null));
        ChecksContext.Equal("商店语言: 未知值 → schinese", SteamStoreLanguage.Default, SteamStoreLanguage.FromUiLanguage("fr-FR"));
        ChecksContext.Check(
            "商店语言: 映射结果都在 Known 里",
            SteamStoreLanguage.Known.Contains(SteamStoreLanguage.FromUiLanguage(UiLanguages.English))
                && SteamStoreLanguage.Known.Contains(SteamStoreLanguage.FromUiLanguage(UiLanguages.SimplifiedChinese)));

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
