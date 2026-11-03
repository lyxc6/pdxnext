using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace PdxLauncher.App.Controls;

/// <summary>
/// PdxDropDown 的自动化 peer。
///
/// ComboBox 的 peer（ComboBoxAutomationPeer）同时提供 Selection 与 ExpandCollapse 模式，
/// 但换成 DropDownButton 后默认只暴露 Invoke 模式，导致 UI 自动化脚本无法展开下拉、
/// 也无法读取当前选中项。这里自行实现 ISelectionProvider 与 IExpandCollapseProvider，
/// 让 PdxDropDown 在自动化层面与 ComboBox 保持一致。
/// </summary>
internal sealed class PdxDropDownAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider, ISelectionProvider
{
    private readonly PdxDropDown _owner;

    public PdxDropDownAutomationPeer(PdxDropDown owner)
        : base(owner)
    {
        _owner = owner;
    }

    protected override string GetClassNameCore() => nameof(PdxDropDown);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;

    protected override string GetNameCore() => _owner.GetItemDisplayText(_owner.SelectedItem);

    protected override object GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.ExpandCollapse || patternInterface == PatternInterface.Selection)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    protected override IList<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        var list = _owner.CreateItemsListPeer();
        if (list is not null)
        {
            children.Add(list);
        }

        return children;
    }

    public ExpandCollapseState ExpandCollapseState =>
        _owner.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => _owner.OpenDropDown();

    public void Collapse() => _owner.CloseDropDown();

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => true;

    public IRawElementProviderSimple[] GetSelection()
    {
        var selected = _owner.CreateSelectedItemAutomationPeer();
        if (selected is null)
        {
            return Array.Empty<IRawElementProviderSimple>();
        }

        return new[] { ProviderFromPeer(selected) };
    }
}
