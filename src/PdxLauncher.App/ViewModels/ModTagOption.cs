namespace PdxLauncher.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;

/// <summary>上传面板里的一个标签复选项。</summary>
public sealed partial class ModTagOption : LocalizedObservableObject
{
    /// <summary>创建选项。</summary>
    public ModTagOption(string name, bool isSelected = false)
    {
        Name = name;
        _isSelected = isSelected;
    }

    /// <summary>标签名（发给 Steam 的机器名，不翻译）。</summary>
    public string Name { get; }

    /// <summary>界面展示名：按当前语言取译文，未收录的标签原样显示机器名。</summary>
    public string DisplayName => WorkshopTagText.Localize(Name);

    /// <summary>是否选中。</summary>
    [ObservableProperty]
    private bool _isSelected;
}
