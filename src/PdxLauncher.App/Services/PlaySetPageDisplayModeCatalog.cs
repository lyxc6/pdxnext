namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;

/// <summary>设置页「播放集页面显示方式」下拉框的一项。</summary>
public sealed class PlaySetPageDisplayModeOption : LocalizedObservableObject
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    internal PlaySetPageDisplayModeOption(string key, string titleKey, string descriptionKey)
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
/// 播放集页面显示方式的可选项目录（取值真源在 <see cref="PlaySetPageDisplayModes"/>）。
/// Resolve 返回目录中的同一实例，下拉控件的 SelectedItem 才能按引用相等匹配。
/// </summary>
public static class PlaySetPageDisplayModeCatalog
{
    public static IReadOnlyList<PlaySetPageDisplayModeOption> All { get; } = new[]
    {
        new PlaySetPageDisplayModeOption(
            PlaySetPageDisplayModes.OfficialLauncher,
            Keys.SettingsInterfacePlaysetOfficialTitle,
            Keys.SettingsInterfacePlaysetOfficialDesc),
        new PlaySetPageDisplayModeOption(
            PlaySetPageDisplayModes.Placeholder,
            Keys.SettingsInterfacePlaysetPlaceholderTitle,
            Keys.SettingsInterfacePlaysetPlaceholderDesc),
    };

    /// <summary>按设置值找到目录中的同一实例；未知值回退第一项（原版启动器）。</summary>
    public static PlaySetPageDisplayModeOption Resolve(string? key)
    {
        var normalized = PlaySetPageDisplayModes.Normalize(key);
        return All.FirstOrDefault(option => string.Equals(option.Key, normalized, StringComparison.Ordinal))
            ?? All[0];
    }
}
