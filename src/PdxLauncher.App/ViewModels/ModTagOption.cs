namespace PdxLauncher.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>上传面板里的一个标签复选项。</summary>
public sealed partial class ModTagOption : ObservableObject
{
    /// <summary>创建选项。</summary>
    public ModTagOption(string name, bool isSelected = false)
    {
        Name = name;
        _isSelected = isSelected;
    }

    /// <summary>标签名（游戏数据，不翻译）。</summary>
    public string Name { get; }

    /// <summary>是否选中。</summary>
    [ObservableProperty]
    private bool _isSelected;
}
