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

    /// <summary>
    /// 双列布局（照官方「模组合集」编辑器）：左栏是 mod 库里还没加入当前播放集的 mod（勾选即加入），
    /// 右栏是当前播放集的 mod（顺序 / 启用 / 移除）。与 <see cref="OfficialLauncher"/> 互不影响，
    /// 「添加模组」子页仍只属于原版启动器布局。
    /// </summary>
    public const string TwoColumn = "twoColumn";

    /// <summary>预留的留空布局：播放集页面暂不显示任何内容。</summary>
    public const string Placeholder = "placeholder";

    /// <summary>把任意设置值归一化成合法取值；null / 空白 / 未知值一律回退 <see cref="OfficialLauncher"/>。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OfficialLauncher;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, TwoColumn, StringComparison.OrdinalIgnoreCase))
        {
            return TwoColumn;
        }

        return string.Equals(trimmed, Placeholder, StringComparison.OrdinalIgnoreCase)
            ? Placeholder
            : OfficialLauncher;
    }

    /// <summary>是否为留空布局。</summary>
    public static bool IsPlaceholder(string? value) =>
        string.Equals(Normalize(value), Placeholder, StringComparison.Ordinal);

    /// <summary>是否为双列布局。</summary>
    public static bool IsTwoColumn(string? value) =>
        string.Equals(Normalize(value), TwoColumn, StringComparison.Ordinal);
}
