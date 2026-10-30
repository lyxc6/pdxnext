namespace PdxLauncher.Core.Online.Translation;

using PdxLauncher.Core.Configuration;

/// <summary>
/// 一次翻译任务的选项快照：从设置里读出来、归一化之后传给引擎与缓存。
/// </summary>
/// <param name="Enabled">翻译总开关。</param>
/// <param name="EngineMode">引擎模式（<see cref="TranslationEngineModes"/>）。</param>
/// <param name="BaseUrl">自填 API 的根地址；null 表示未配置。</param>
/// <param name="ApiKey">自填 API 的密钥；null 表示未配置。</param>
/// <param name="Model">自填 API 的模型名；空时用 <see cref="TranslationModels.Default"/>。</param>
/// <param name="TargetLanguage">目标语言代码（zh-CN / en-US）。</param>
/// <param name="CacheDays">译文缓存保留天数。</param>
public readonly record struct TranslationOptions(
    bool Enabled,
    string EngineMode,
    string? BaseUrl,
    string? ApiKey,
    string Model,
    string TargetLanguage,
    int CacheDays)
{
    /// <summary>中文目标语言代码。</summary>
    public const string SimplifiedChinese = "zh-CN";

    /// <summary>英文目标语言代码。</summary>
    public const string English = "en-US";

    /// <summary>
    /// 从设置与界面语言生成选项：目标语言留空时跟随界面语言；
    /// 引擎模式、模型名、缓存天数都会归一化，未知值不会让翻译崩掉。
    /// </summary>
    /// <param name="settings">应用设置。</param>
    /// <param name="uiLanguage">当前界面语言（zh-CN / en-US）。</param>
    public static TranslationOptions FromSettings(AppSettings? settings, string? uiLanguage)
    {
        if (settings is null)
        {
            return new TranslationOptions(
                false,
                TranslationEngineModes.Auto,
                null,
                null,
                TranslationModels.Default,
                ResolveTargetLanguage(uiLanguage),
                30);
        }

        var target = string.IsNullOrWhiteSpace(settings.TranslateTargetLanguage)
            ? ResolveTargetLanguage(uiLanguage)
            : NormalizeTargetLanguage(settings.TranslateTargetLanguage);

        var model = string.IsNullOrWhiteSpace(settings.TranslateModel)
            ? TranslationModels.Default
            : settings.TranslateModel.Trim();

        return new TranslationOptions(
            settings.TranslateEnabled,
            TranslationEngineModes.Normalize(settings.TranslateEngineMode),
            string.IsNullOrWhiteSpace(settings.TranslateBaseUrl) ? null : settings.TranslateBaseUrl.Trim(),
            string.IsNullOrWhiteSpace(settings.TranslateApiKey) ? null : settings.TranslateApiKey.Trim(),
            model,
            target,
            Math.Clamp(settings.TranslateCacheDays, 1, 365));
    }

    /// <summary>自填引擎是否已经配置齐（地址 + 密钥 + 模型）。</summary>
    public bool HasCustomConfiguration =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// 目标语言归一化：只认 zh-CN 与 en-US，其余按界面语言回落。
    /// </summary>
    /// <param name="value">用户填的语言。</param>
    public static string NormalizeTargetLanguage(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return SimplifiedChinese;
        }

        if (trimmed.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return SimplifiedChinese;
        }

        if (trimmed.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return English;
        }

        return SimplifiedChinese;
    }

    /// <summary>界面语言归一化成目标语言（zh 开头→中文，其余→英文）。</summary>
    /// <param name="uiLanguage">界面语言。</param>
    public static string ResolveTargetLanguage(string? uiLanguage)
    {
        var trimmed = uiLanguage?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return SimplifiedChinese;
        }

        return trimmed.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChinese
            : English;
    }
}
