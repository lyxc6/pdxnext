namespace PdxLauncher.App.Controls;

using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

/// <summary>
/// <see cref="PdxTagChip"/> 的自动化 peer。
/// <para>
/// 胶囊视觉上是三态，但基类是 <see cref="Button"/>，默认只暴露 Invoke 模式，读屏说不出
/// 「已包含 / 已排除」。这里补一个 <see cref="IToggleProvider"/>：On = 必须包含、
/// Indeterminate = 必须排除、Off = 未选；Toggle() 等价于点一下（按 Cycle 轮换）。
/// </para>
/// </summary>
internal sealed class PdxTagChipAutomationPeer : ButtonAutomationPeer, IToggleProvider
{
    private readonly PdxTagChip _owner;

    public PdxTagChipAutomationPeer(PdxTagChip owner)
        : base(owner)
    {
        _owner = owner;
    }

    public ToggleState ToggleState => _owner.IsRequired
        ? ToggleState.On
        : _owner.IsExcluded
            ? ToggleState.Indeterminate
            : ToggleState.Off;

    public void Toggle() => _owner.RequestToggle();

    protected override object GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Toggle)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }
}
