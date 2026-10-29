namespace PdxLauncher.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;

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

    /// <summary>
    /// 全屏查看器里这张图可用的最大宽度（DIP）。
    /// <para>
    /// 由页面按浮层尺寸算好写进来，模板直接 <c>x:Bind</c>；默认无穷大表示「还没算过」，
    /// 此时保持图片自己的尺寸，不会把图压成 0。
    /// </para>
    /// </summary>
    [ObservableProperty]
    private double _viewerMaxWidth = double.PositiveInfinity;

    /// <summary>全屏查看器里这张图可用的最大高度（DIP）。</summary>
    [ObservableProperty]
    private double _viewerMaxHeight = double.PositiveInfinity;

    /// <summary>写入查看器里可用的宽高（由 <see cref="WorkshopGalleryLayout.ViewerContent"/> 算出来）。</summary>
    /// <param name="width">最大宽度（DIP）。</param>
    /// <param name="height">最大高度（DIP）。</param>
    public void ApplyViewerSize(double width, double height)
    {
        ViewerMaxWidth = width;
        ViewerMaxHeight = height;
    }

    partial void OnPathChanged(string? value) => OnPropertyChanged(nameof(HasPath));
}
