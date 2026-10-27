namespace PdxLauncher.Core.Configuration;

/// <summary>
/// 播放集页面显示方式的取值（settings.json 的 playSetPageDisplayMode 字段）。
/// 用字符串常量而不是枚举：AppSettingsStore 的 JSON 反序列化遇到未知枚举值会抛异常，
/// 进而让整份设置回退默认值；字符串可以安全兜底。
/// </summary>
public static class PlaySetPageDisplayModes
{
    /// <summary>原版启动器布局：播放集切换器在工具条，所有改动自动保存并写入加载文件（默认）。</summary>
    public const string OfficialLauncher = "officialLauncher";

    /// <summary>预留的留空布局：播放集页面暂不显示任何内容。</summary>
    public const string Placeholder = "placeholder";

    /// <summary>把任意设置值归一化成合法取值；null / 空白 / 未知值一律回退 <see cref="OfficialLauncher"/>。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OfficialLauncher;
        }

        return string.Equals(value.Trim(), Placeholder, StringComparison.OrdinalIgnoreCase)
            ? Placeholder
            : OfficialLauncher;
    }

    /// <summary>是否为留空布局。</summary>
    public static bool IsPlaceholder(string? value) =>
        string.Equals(Normalize(value), Placeholder, StringComparison.Ordinal);
}
