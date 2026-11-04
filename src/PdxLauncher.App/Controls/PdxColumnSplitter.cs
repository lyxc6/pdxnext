namespace PdxLauncher.App.Controls;

using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// 双列布局中间的分隔条：按住左右拖动改变两栏宽度，双击复位。
/// <para>
/// WinUI 3 的 <c>Thumb</c> 是 sealed，不能继承，所以这里直接拿 <see cref="Grid"/> 当命中面，
/// 自己处理指针捕获：按下捕获、移动按「相对上一次」的位移回调、松开或丢失捕获收尾。
/// 之所以给增量而不是相对按下点的绝对位移，是因为页面那边只需要「当前宽度 + 增量」，
/// 不必记住拖动开始时的宽度。
/// </para>
/// <para>
/// 光标只能靠这个薄子类设置：<c>ProtectedCursor</c> 是 protected，而 WinUI 3 没有公开的
/// Cursor 属性——没有左右箭头光标，用户不会想到这条线能拖。
/// </para>
/// </summary>
public sealed class PdxColumnSplitter : Grid
{
    private uint _pointerId;
    private double _lastX;
    private bool _dragging;

    public PdxColumnSplitter()
    {
        // 透明背景既是不可见的命中区，又让 Grid 能接住指针（没有背景的空面板不参与命中测试）。
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        // WinUI 3 的 UIElement 没有可重写的 OnPointerPressed 系列（CS0115），只能挂事件。
        PointerPressed += OnSplitterPointerPressed;
        PointerMoved += OnSplitterPointerMoved;
        PointerReleased += OnSplitterPointerReleased;
        PointerCaptureLost += OnSplitterPointerCaptureLost;
        DoubleTapped += OnSplitterDoubleTapped;
    }

    /// <summary>拖动中每次指针移动触发一次，参数是相对上一次的水平位移（DIP，向右为正）。</summary>
    public event EventHandler<double>? Dragged;

    /// <summary>双击请求把宽度复位到默认值。</summary>
    public event EventHandler? ResetRequested;

    private void OnSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pointerId = point.PointerId;
        _lastX = RootX(e);
        _dragging = CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        var x = RootX(e);
        var delta = x - _lastX;
        _lastX = x;
        if (delta != 0)
        {
            // 拖动期间分栏宽度变化会让本控件跟着平移，但增量语义不受影响：
            // 指针被捕获后坐标仍相对本控件，累计位移与「宽度累计变化」始终一致。
            Dragged?.Invoke(this, delta);
        }

        e.Handled = true;
    }

    private void OnSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnSplitterPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
    }

    /// <summary>
    /// 取相对窗口根部（null = 页面根）的水平坐标。
    /// 不能用 <c>GetCurrentPoint(this)</c>：拖动时本控件跟着分栏一起平移，
    /// 控件内坐标会把「指针移动」和「控件移动」抵消掉，增量方向反过来。
    /// 根部容器在整个拖动过程中不动，才是稳定的参考系。
    /// </summary>
    private double RootX(PointerRoutedEventArgs e) => e.GetCurrentPoint(null).Position.X;

    private void OnSplitterDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ResetRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
