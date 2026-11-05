namespace PdxLauncher.App.Controls;

using System;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.System;

/// <summary>
/// 打开浮层时把键盘焦点放到哪个键上。
///
/// 默认是确认键，与历史行为一致；删除、覆盖、清空这类破坏性操作应传
/// <see cref="Cancel"/>，让用户按 Enter 不会误执行。
/// </summary>
public enum InitialFocus
{
    /// <summary>默认焦点在确认键（Enter = 确认）。</summary>
    Confirm,

    /// <summary>默认焦点在取消键（Enter = 取消，只有点击或 Tab 到确认键才会执行）。</summary>
    Cancel,
}

/// <summary>
/// 窗口级「确认 / 取消」浮层：自绘对话框，把「取消」放在左边、「确认」放在右边。
///
/// 为什么不用 WinUI 的 <c>ContentDialog</c>：它的模板把 PrimaryButton 固定在命令区第 0 列、
/// CloseButton 固定在第 4 列，视觉顺序永远是「确认在左、取消在右」，框架没有公开的交换能力
/// （见 generic.xaml 的 DefaultContentDialogStyle）。自己画按钮才能换位。
///
/// 语义：
/// <list type="bullet">
///   <item>Esc = 取消；Enter 执行初始焦点对应的动作（多行输入框里 Enter 仍然换行）。</item>
///   <item>确认键是 Accent 主色；默认焦点在确认键，破坏性操作可改为取消键。</item>
///   <item>点遮罩不关闭；关闭后焦点回投到打开它的那个控件。</item>
///   <item>同一时刻只能有一个浮层：重复调用按「取消」处理，不叠加。</item>
/// </list>
/// </summary>
public sealed partial class ConfirmOverlay : UserControl
{
    /// <summary>当前窗口上的浮层实例（由 <see cref="MainWindow"/> 在构造时登记）。</summary>
    public static ConfirmOverlay? Host { get; internal set; }

    private TaskCompletionSource<bool>? _completion;
    private Action? _onConfirm;
    private DependencyObject? _focusReturnTarget;
    private InitialFocus _initialFocus = InitialFocus.Confirm;

    public ConfirmOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 便捷入口：走当前窗口上的全局浮层。宿主不存在（例如窗口还没建起来）时按「取消」处理。
    /// </summary>
    public static Task<bool> ConfirmAsync(
        string title,
        object content,
        string confirmText,
        string? cancelText = null,
        Action? onConfirm = null,
        bool isConfirmEnabled = true,
        InitialFocus initialFocus = InitialFocus.Confirm)
    {
        var host = Host;
        return host is null
            ? Task.FromResult(false)
            : host.ShowAsync(
                title,
                content,
                confirmText,
                cancelText,
                onConfirm,
                isConfirmEnabled,
                initialFocus);
    }

    /// <summary>便捷入口：只有一个「关闭」键的消息框。</summary>
    public static Task ShowMessage(string title, object content, string closeText)
    {
        var host = Host;
        return host is null ? Task.CompletedTask : host.ShowMessageAsync(title, content, closeText);
    }

    /// <summary>确认键是否可用（「修复 mod 路径」这类需要按勾选状态动态启停）。</summary>
    public bool IsConfirmEnabled
    {
        get => ConfirmButton.IsEnabled;
        set => ConfirmButton.IsEnabled = value;
    }

    /// <summary>浮层当前是否正在显示。</summary>
    public bool IsOpen => OverlayRoot.Visibility == Visibility.Visible;

    /// <summary>
    /// 由代码关闭浮层（下载完成、宿主退出等）。等价于用户按取消：
    /// 不触发 <c>onConfirm</c>，等待中的调用得到 false。
    /// </summary>
    public void Close()
    {
        if (IsOpen || _completion is not null)
        {
            Complete(confirmed: false);
        }
    }

    /// <summary>
    /// 弹一个两键确认框。返回 true = 用户点了确认；false = 取消（Esc、取消键或重复调用）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="content">正文：字符串会自动包成可换行的 TextBlock，也可以直接给任意 UIElement。</param>
    /// <param name="confirmText">确认键文案（放右边，Accent 主色）。</param>
    /// <param name="cancelText">取消键文案（放左边）；传 null 则只显示一个按钮。</param>
    /// <param name="onConfirm">点确认时同步执行的回调（例如把崩溃详情复制到剪贴板）。</param>
    /// <param name="isConfirmEnabled">确认键初始是否可用。</param>
    /// <param name="initialFocus">
    /// 打开时默认焦点放哪个键。破坏性操作（删除、覆盖、清空）传
    /// <see cref="InitialFocus.Cancel"/>，Enter 也随之变成取消。
    /// </param>
    public Task<bool> ShowAsync(
        string title,
        object content,
        string confirmText,
        string? cancelText = null,
        Action? onConfirm = null,
        bool isConfirmEnabled = true,
        InitialFocus initialFocus = InitialFocus.Confirm)
    {
        // 已有浮层在显示：按取消处理，不叠加第二层。
        if (IsOpen || _completion is not null)
        {
            return Task.FromResult(false);
        }

        _onConfirm = onConfirm;
        _initialFocus = initialFocus;
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        TitleText.Text = title ?? string.Empty;
        TitleText.Visibility = string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;

        ContentHost.Content = content is string text
            ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
            : content;

        CancelButton.Content = cancelText ?? string.Empty;
        CancelButton.Visibility = string.IsNullOrEmpty(cancelText) ? Visibility.Collapsed : Visibility.Visible;

        ConfirmButton.Content = confirmText;
        ConfirmButton.IsEnabled = isConfirmEnabled;

        // 记住打开浮层时焦点所在的控件，关闭后还回去。
        _focusReturnTarget = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;

        OverlayRoot.Visibility = Visibility.Visible;

        // 浮层刚显示时焦点可能还在触发按钮上，等布局完成再移过去。
        var dispatcher = DispatcherQueue;
        if (dispatcher is not null)
        {
            dispatcher.TryEnqueue(FocusInitialTarget);
        }

        return _completion.Task;
    }

    /// <summary>弹一个只有「关闭」键的消息框（按钮右对齐，与 ContentDialog 的单键位置一致）。</summary>
    public Task ShowMessageAsync(string title, object content, string closeText) =>
        ShowAsync(title, content, closeText);

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Complete(confirmed: true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Complete(confirmed: false);
    }

    private void OnContentHostLoaded(object sender, RoutedEventArgs e)
    {
        // 内容加载完才能拿到焦点；输入控件优先，其余按初始焦点决定。
        FocusInitialTarget();
    }

    /// <summary>
    /// 把键盘焦点挪进浮层：内容本身是输入控件时优先聚焦它（例如重命名播放集的输入框），
    /// 否则按 <see cref="_initialFocus"/> 聚焦取消键或确认键。
    ///
    /// 焦点必须落在浮层内部，Esc / Enter 的按键事件才会冒泡到浮层的 KeyDown；
    /// 焦点留在触发按钮上时，键盘语义会整个失效。
    /// </summary>
    private void FocusInitialTarget()
    {
        if (!IsOpen)
        {
            return;
        }

        // 内容本身是输入控件时优先聚焦它（例如重命名播放集的输入框）。
        if (ContentHost.Content is Control focusable
            && IsTextInput(focusable)
            && focusable.Focus(FocusState.Programmatic))
        {
            return;
        }

        if (_initialFocus == InitialFocus.Cancel && CancelButton.Visibility == Visibility.Visible)
        {
            CancelButton.Focus(FocusState.Programmatic);
            return;
        }

        ConfirmButton.Focus(FocusState.Programmatic);
    }

    /// <summary>内容是不是需要键盘输入的控件（这类控件必须优先拿到焦点）。</summary>
    private static bool IsTextInput(Control control) =>
        control is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox;

    private void OnOverlayKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Complete(confirmed: false);
            return;
        }

        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        // 焦点在按钮上时由按钮自己响应，否则会确认两次。
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, ConfirmButton) || ReferenceEquals(focused, CancelButton))
        {
            return;
        }

        // 多行输入框里 Enter 是换行，不当作确认。
        if (focused is TextBox { AcceptsReturn: true })
        {
            return;
        }

        // 初始焦点在取消键时，Enter 与焦点一致走取消；没有取消键（单键消息框）时仍算确认。
        var cancelAvailable = CancelButton.Visibility == Visibility.Visible;
        e.Handled = true;
        Complete(confirmed: !cancelAvailable || _initialFocus == InitialFocus.Confirm);
    }

    private void Complete(bool confirmed)
    {
        var completion = _completion;
        var onConfirm = _onConfirm;
        _completion = null;
        _onConfirm = null;

        OverlayRoot.Visibility = Visibility.Collapsed;
        ContentHost.Content = null;

        // 焦点回投：来源控件可能已经卸载，这时忽略即可。
        var target = _focusReturnTarget;
        _focusReturnTarget = null;
        if (target is Control control)
        {
            try
            {
                control.Focus(FocusState.Programmatic);
            }
            catch (Exception)
            {
                // 来源控件已被卸载或不可聚焦：忽略。
            }
        }

        if (confirmed && onConfirm is not null)
        {
            try
            {
                onConfirm();
            }
            catch (Exception)
            {
                // 回调失败不影响「确认」这个结论，也不让浮层卡住。
            }
        }

        completion?.TrySetResult(confirmed);
    }
}
