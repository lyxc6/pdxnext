namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml.Media;
using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;

/// <summary>设置页「背景显示方式」下拉框的一项（不含 XAML 类型，ViewModel 可以直接持有）。</summary>
public sealed class BackgroundDisplayModeOption : LocalizedObservableObject
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    internal BackgroundDisplayModeOption(string key, string titleKey, string descriptionKey)
    {
        Key = key;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
    }

    public string Key { get; }

    public string Title => T(_titleKey);

    /// <summary>下拉项的 UI 自动化名称取自 ToString，返回本地化标题。</summary>
    public override string ToString() => Title;

    public string Description => T(_descriptionKey);
}

/// <summary>
/// 整窗背景显示方式的可选项目录（取值真源在 <see cref="BackgroundDisplayModes"/>）。
/// Resolve 返回目录中的同一实例，下拉控件的 SelectedItem 才能按引用相等匹配。
/// 具体落到 <see cref="ImageBrush"/> 上的 Stretch / 对齐由 <see cref="ApplyTo"/> 决定。
/// </summary>
public static class BackgroundDisplayModeCatalog
{
    public static IReadOnlyList<BackgroundDisplayModeOption> All { get; } = new[]
    {
        new BackgroundDisplayModeOption(
            BackgroundDisplayModes.Cover,
            Keys.SettingsInterfaceBackgroundCoverTitle,
            Keys.SettingsInterfaceBackgroundCoverDesc),
        new BackgroundDisplayModeOption(
            BackgroundDisplayModes.Fill,
            Keys.SettingsInterfaceBackgroundFillTitle,
            Keys.SettingsInterfaceBackgroundFillDesc),
        new BackgroundDisplayModeOption(
            BackgroundDisplayModes.FullBlur,
            Keys.SettingsInterfaceBackgroundFullBlurTitle,
            Keys.SettingsInterfaceBackgroundFullBlurDesc),
        new BackgroundDisplayModeOption(
            BackgroundDisplayModes.FullPlain,
            Keys.SettingsInterfaceBackgroundFullPlainTitle,
            Keys.SettingsInterfaceBackgroundFullPlainDesc),
        new BackgroundDisplayModeOption(
            BackgroundDisplayModes.Center,
            Keys.SettingsInterfaceBackgroundCenterTitle,
            Keys.SettingsInterfaceBackgroundCenterDesc),
    };

    /// <summary>按设置值找到目录中的同一实例；未知值回退第一项（覆盖）。</summary>
    public static BackgroundDisplayModeOption Resolve(string? key)
    {
        var normalized = BackgroundDisplayModes.Normalize(key);
        return All.FirstOrDefault(option => string.Equals(option.Key, normalized, StringComparison.Ordinal))
            ?? All[0];
    }

    /// <summary>该取值是否需要额外渲染一层同图模糊铺底。</summary>
    public static bool UsesBlurredBackdrop(string? key) => BackgroundDisplayModes.UsesBlurredBackdrop(key);

    /// <summary>
    /// 把档位换算成 <see cref="ImageBrush"/> 的 Stretch 与对齐。
    /// 覆盖档取 Left/Center，复刻官方 CSS 的 background-size:cover + background-position-x:left。
    /// </summary>
    public static void ApplyTo(ImageBrush brush, string? key)
    {
        ArgumentNullException.ThrowIfNull(brush);

        var (stretch, alignmentX) = BackgroundDisplayModes.Normalize(key) switch
        {
            BackgroundDisplayModes.Fill => (Stretch.Fill, AlignmentX.Center),
            BackgroundDisplayModes.FullBlur => (Stretch.Uniform, AlignmentX.Center),
            BackgroundDisplayModes.FullPlain => (Stretch.Uniform, AlignmentX.Center),
            BackgroundDisplayModes.Center => (Stretch.None, AlignmentX.Center),
            _ => (Stretch.UniformToFill, AlignmentX.Left),
        };

        brush.Stretch = stretch;
        brush.AlignmentX = alignmentX;
        brush.AlignmentY = AlignmentY.Center;
    }
}
