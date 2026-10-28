namespace PdxLauncher.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;

/// <summary>
/// 工坊详情里的一张截图：先占位，下载完成把本地路径填进 <see cref="Path"/>，
/// 界面靠 <see cref="HasPath"/> 在占位图案与真图之间切换。
/// </summary>
public sealed partial class WorkshopScreenshotViewModel : LocalizedObservableObject
{
    /// <summary>构造。</summary>
    /// <param name="index">在截图列表里的序号（决定缓存文件名与 AutomationId）。</param>
    /// <param name="url">远端地址。</param>
    public WorkshopScreenshotViewModel(int index, string url)
    {
        Index = index;
        Url = url;
    }

    /// <summary>序号（0 基）。</summary>
    public int Index { get; }

    /// <summary>远端地址。</summary>
    public string Url { get; }

    /// <summary>自动化测试用的标识。</summary>
    public string AutomationId => "workshopDetailShot_" + Index;

    /// <summary>本地路径；下载完成或命中缓存后才非空。</summary>
    [ObservableProperty]
    private string? _path;

    /// <summary>本地图是否就绪。</summary>
    public bool HasPath => !string.IsNullOrWhiteSpace(Path);

    /// <summary>是否是内联大图当前显示的那一张（缩略图条据此加描边）。</summary>
    [ObservableProperty]
    private bool _isCurrent;

    partial void OnPathChanged(string? value) => OnPropertyChanged(nameof(HasPath));
}
