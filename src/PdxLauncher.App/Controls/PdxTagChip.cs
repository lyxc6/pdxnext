namespace PdxLauncher.App.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

/// <summary>
/// 筛选栏里的三态胶囊：未选 / 必须包含 / 必须排除。
/// <para>
/// 为什么不用现成控件：<see cref="ToggleButton"/> 只有开/关两态，<see cref="CheckBox"/> 的三态
/// 又只能表达「不确定」，和「必须排除」语义对不上；WinUI 也没有 Chip 控件。
/// 所以这里用 <see cref="Button"/> + 三组视觉状态自己画。
/// </para>
/// <para>
/// 控件只负责显示：业务状态仍由 ViewModel 的两个互斥布尔量驱动，点击后由页面调用
/// <c>WorkshopTagOption.Cycle()</c> 轮换（未选 → 包含 → 排除 → 未选），控件本身不改状态。
/// </para>
/// </summary>
public sealed class PdxTagChip : Button
{
    /// <summary><see cref="Text"/> 的依赖属性。</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(PdxTagChip),
        new PropertyMetadata(string.Empty, OnDisplayStateChanged));

    /// <summary><see cref="IsRequired"/> 的依赖属性。</summary>
    public static readonly DependencyProperty IsRequiredProperty = DependencyProperty.Register(
        nameof(IsRequired),
        typeof(bool),
        typeof(PdxTagChip),
        new PropertyMetadata(false, OnDisplayStateChanged));

    /// <summary><see cref="IsExcluded"/> 的依赖属性。</summary>
    public static readonly DependencyProperty IsExcludedProperty = DependencyProperty.Register(
        nameof(IsExcluded),
        typeof(bool),
        typeof(PdxTagChip),
        new PropertyMetadata(false, OnDisplayStateChanged));

    /// <summary>创建胶囊。</summary>
    public PdxTagChip()
    {
        DefaultStyleKey = typeof(PdxTagChip);
    }

    /// <summary>
    /// 读屏 / UI 自动化要求轮换状态时触发（等价于点一下）。
    /// <para>
    /// 用来给自动化 Toggle 模式一个出口：Button 的 Click 不是可重抛的路由事件，
    /// peer 拿不到「按一下」的入口，所以单独开一个和 Click 同签名的事件，页面把两者接同一个处理器。
    /// </para>
    /// </summary>
    public event RoutedEventHandler? ToggleRequested;

    /// <summary>胶囊文字（标签名 / DLC 名）。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>是否必须包含（视觉状态 Include）。</summary>
    public bool IsRequired
    {
        get => (bool)GetValue(IsRequiredProperty);
        set => SetValue(IsRequiredProperty, value);
    }

    /// <summary>是否必须排除（视觉状态 Exclude）。</summary>
    public bool IsExcluded
    {
        get => (bool)GetValue(IsExcludedProperty);
        set => SetValue(IsExcludedProperty, value);
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new PdxTagChipAutomationPeer(this);

    /// <summary>自动化 Toggle 模式的入口（由 peer 调用）。</summary>
    internal void RequestToggle() => ToggleRequested?.Invoke(this, new RoutedEventArgs());

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateTagState(useTransitions: false);
    }

    /// <summary>当前应显示的状态名（与 <c>PdxTagChip.xaml</c> 里的 VisualState 同名）。</summary>
    private string TagStateName => IsRequired
        ? "Include"
        : IsExcluded
            ? "Exclude"
            : "None";

    private static void OnDisplayStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PdxTagChip)d).UpdateTagState(useTransitions: true);

    private void UpdateTagState(bool useTransitions) =>
        VisualStateManager.GoToState(this, TagStateName, useTransitions);
}
