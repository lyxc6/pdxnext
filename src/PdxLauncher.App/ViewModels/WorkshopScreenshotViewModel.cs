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

    /// <summary>
    /// 这一张还在下载（创建即为 true，下载结束由 <see cref="MarkSettled"/> 置 false）。
    /// <para>
    /// 它只影响「这张图自己的叠层」，不再由页面级批次标志推断 ——
    /// 这样后面的截图先下完时，选中却还没下完的那张会显示「正在下载这张截图…」，
    /// 而不是一块什么都不说的黑。
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _isPending = true;

    /// <summary>这一张下载失败（异常路径置 true；有本地路径时永远为 false）。</summary>
    [ObservableProperty]
    private bool _isFailed;

    /// <summary>大图区域显示「正在下载这张截图…」叠层。</summary>
    public bool ShowPending => IsPending && !HasPath;

    /// <summary>大图区域显示「截图下载失败」叠层。</summary>
    public bool ShowFailed => IsFailed && !HasPath;

    /// <summary>既没在下载、也没失败、又没本地图时显示中性占位图案。</summary>
    public bool ShowPlaceholder => !HasPath && !IsPending && !IsFailed;

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

    /// <summary>这一张的下载尝试结束（成功、失败或取消都算）。</summary>
    /// <param name="failed">本次是否因异常失败；已有本地路径时按成功处理。</param>
    public void MarkSettled(bool failed)
    {
        IsFailed = failed && !HasPath;
        IsPending = false;
    }

    partial void OnPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPath));
        OnPropertyChanged(nameof(ShowPending));
        OnPropertyChanged(nameof(ShowFailed));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }

    partial void OnIsPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPending));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }

    partial void OnIsFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFailed));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }
}
