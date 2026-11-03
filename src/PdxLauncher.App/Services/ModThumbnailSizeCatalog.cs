namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;

/// <summary>设置页「Mod 图片大小」下拉框的一项。</summary>
public sealed class ModThumbnailSizeOption : LocalizedObservableObject
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    internal ModThumbnailSizeOption(string key, string titleKey, string descriptionKey, int pixels)
    {
        Key = key;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        Pixels = pixels;
    }

    public string Key { get; }

    public int Pixels { get; }

    public string Title => T(_titleKey);

    /// <summary>下拉项的 UI 自动化名称取自 ToString，返回本地化标题。</summary>
    public override string ToString() => Title;

    public string Description => T(_descriptionKey);
}

/// <summary>
/// Mod 缩略图尺寸的可选项目录（取值真源在 <see cref="ModThumbnailSizes"/>）。
/// Resolve 返回目录中的同一实例，下拉控件的 SelectedItem 才能按引用相等匹配。
/// </summary>
public static class ModThumbnailSizeCatalog
{
    public static IReadOnlyList<ModThumbnailSizeOption> All { get; } = new[]
    {
        new ModThumbnailSizeOption(
            ModThumbnailSizes.Small,
            Keys.SettingsInterfaceSizeSmallTitle,
            Keys.SettingsInterfaceSizeSmallDesc,
            ModThumbnailSizes.SmallPixels),
        new ModThumbnailSizeOption(
            ModThumbnailSizes.Medium,
            Keys.SettingsInterfaceSizeMediumTitle,
            Keys.SettingsInterfaceSizeMediumDesc,
            ModThumbnailSizes.MediumPixels),
        new ModThumbnailSizeOption(
            ModThumbnailSizes.Large,
            Keys.SettingsInterfaceSizeLargeTitle,
            Keys.SettingsInterfaceSizeLargeDesc,
            ModThumbnailSizes.LargePixels),
    };

    /// <summary>按设置值找到目录中的同一实例；未知值回退第二项（中）。</summary>
    public static ModThumbnailSizeOption Resolve(string? key)
    {
        var normalized = ModThumbnailSizes.Normalize(key);
        return All.FirstOrDefault(option => string.Equals(option.Key, normalized, StringComparison.Ordinal))
            ?? All[1];
    }
}
