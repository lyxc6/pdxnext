using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace PdxLauncher.App.Controls;

/// <summary>
/// 统一下拉控件：用 DropDownButton + Flyout + ListView 组合，替代 ComboBox。
/// 之所以不用 ComboBox，是因为 ComboBox 的下拉列表永远把选中项居中展示，
/// 选中最后一项时下拉会向上展开，且 MaxDropDownHeight 无法改变这一行为。
/// </summary>
public partial class PdxDropDown : UserControl
{
    private static readonly Dictionary<Type, PropertyInfo> MemberCache = new();
    private static readonly object MemberCacheLock = new();

    private bool _isSyncing;
    private PdxDropDownAutomationPeer? _peer;
    private INotifyPropertyChanged? _observedItem;
    private object? _lastNotifiedItem;

    public PdxDropDown()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(object),
        typeof(PdxDropDown),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(PdxDropDown),
        new PropertyMetadata(null, OnSelectedItemChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath),
        typeof(string),
        typeof(PdxDropDown),
        new PropertyMetadata(null, OnDisplayMemberPathChanged));

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(PdxDropDown),
        new PropertyMetadata(null, OnItemTemplateChanged));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(PdxDropDown),
        new PropertyMetadata(null, OnPlaceholderTextChanged));

    public static readonly DependencyProperty DropDownWidthProperty = DependencyProperty.Register(
        nameof(DropDownWidth),
        typeof(double),
        typeof(PdxDropDown),
        new PropertyMetadata(double.NaN, OnDropDownWidthChanged));

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string? DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>下拉面板宽度；NaN 表示与按钮等宽。</summary>
    public double DropDownWidth
    {
        get => (double)GetValue(DropDownWidthProperty);
        set => SetValue(DropDownWidthProperty, value);
    }

    public bool IsDropDownOpen => Button.Flyout is FlyoutBase flyout && flyout.IsOpen;

    /// <summary>用户在下拉列表中做出选择时触发，参数为被选中的项。</summary>
    public event EventHandler<PdxDropDownSelectionChangedEventArgs>? SelectionChanged;

    protected override AutomationPeer OnCreateAutomationPeer() => Peer;

    internal PdxDropDownAutomationPeer Peer => _peer ??= new PdxDropDownAutomationPeer(this);

    internal void OpenDropDown()
    {
        if (Button.Flyout is not FlyoutBase flyout || flyout.IsOpen)
        {
            return;
        }

        flyout.ShowAt(Button);
    }

    internal void CloseDropDown()
    {
        if (Button.Flyout is not FlyoutBase flyout || !flyout.IsOpen)
        {
            return;
        }

        flyout.Hide();
    }

    internal void SelectItemFromAutomation(object item)
    {
        SelectedItem = item;
        CloseDropDown();
        RaiseSelectionChanged(item);
    }

    internal string GetItemDisplayText(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        var path = DisplayMemberPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var value = ReadMember(item, path);
            if (value is not null)
            {
                return Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
            }
        }

        return item.ToString() ?? string.Empty;
    }

    /// <summary>
    /// 下拉列表（Flyout 里的 ListView）的自动化 peer。
    ///
    /// Flyout 内容位于独立的弹窗宿主中，默认不会出现在下拉按钮的自动化子树里，
    /// 于是 UI 自动化脚本用 <c>FindAll(Descendants, ListItem)</c> 枚举不到任何选项。
    /// 这里把 ListView 的 peer 显式挂为下拉控件的子节点，
    /// 选项就会以原生 ListItem 形态出现在下拉控件的自动化子树中，
    /// 并保留 WinUI 原生的 SelectionItem 模式（可被脚本 Select）。
    /// </summary>
    internal AutomationPeer? CreateItemsListPeer() =>
        FrameworkElementAutomationPeer.CreatePeerForElement(ItemsList);

    /// <summary>
    /// 找出下拉列表里已经实现（realized）的条目容器。
    /// </summary>
    private List<FrameworkElement?> FindItemContainers()
    {
        var containers = new List<FrameworkElement?>();
        CollectItemContainers(ItemsList, containers);
        return containers;
    }

    private static void CollectItemContainers(DependencyObject root, List<FrameworkElement?> containers)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ListViewItem container)
            {
                containers.Add(container);
            }

            CollectItemContainers(child, containers);
        }
    }

    /// <summary>
    /// 构造当前选中项的自动化 peer，供 Selection 模式返回。
    /// 下拉已展开、条目已实现时复用真实条目的 peer（运行时 ID 唯一）；
    /// 未展开时返回 null，由调用方回退到控件自身的名称。
    /// </summary>
    internal AutomationPeer? CreateSelectedItemAutomationPeer()
    {
        var selected = SelectedItem;
        if (selected is null)
        {
            return null;
        }

        foreach (var container in FindItemContainers())
        {
            if (container is ListViewItem listItem && Equals(listItem.Content, selected))
            {
                return FrameworkElementAutomationPeer.CreatePeerForElement(listItem);
            }
        }

        return null;
    }

    private static object? ReadMember(object item, string path)
    {
        var type = item.GetType();
        PropertyInfo? property;
        lock (MemberCacheLock)
        {
            if (!MemberCache.TryGetValue(type, out property))
            {
                property = type.GetProperty(path, BindingFlags.Public | BindingFlags.Instance);
                MemberCache[type] = property!;
            }
        }

        return property?.GetValue(item);
    }

    private static void OnItemsSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PdxDropDown dropdown)
        {
            return;
        }

        dropdown.ItemsList.ItemsSource = args.NewValue as IEnumerable;
        dropdown.SyncListSelection();
        dropdown.UpdateContent();
    }

    private static void OnSelectedItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PdxDropDown dropdown)
        {
            return;
        }

        dropdown.ObserveSelectedItem(args.NewValue);
        dropdown.SyncListSelection();
        dropdown.UpdateContent();
    }

    /// <summary>
    /// 订阅选中项的属性变化。
    /// 选中项通常是可本地化的目录对象（实现 <see cref="INotifyPropertyChanged"/>），
    /// 切换界面语言后它的显示文本会变，这里跟着刷新按钮上的文字。
    /// </summary>
    private void ObserveSelectedItem(object? item)
    {
        if (ReferenceEquals(_observedItem, item))
        {
            return;
        }

        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged -= OnSelectedItemPropertyChanged;
        }

        _observedItem = item as INotifyPropertyChanged;
        if (_observedItem is not null)
        {
            _observedItem.PropertyChanged += OnSelectedItemPropertyChanged;
        }
    }

    private void OnSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateContent();
    }

    private static void OnDisplayMemberPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PdxDropDown dropdown)
        {
            return;
        }

        dropdown.ItemsList.DisplayMemberPath = args.NewValue as string;
        dropdown.UpdateContent();
    }

    private static void OnItemTemplateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PdxDropDown dropdown)
        {
            return;
        }

        dropdown.ItemsList.ItemTemplate = args.NewValue as DataTemplate;
        dropdown.UpdateContent();
    }

    private static void OnPlaceholderTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is PdxDropDown dropdown)
        {
            dropdown.UpdateContent();
        }
    }

    private static void OnDropDownWidthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is PdxDropDown dropdown)
        {
            dropdown.UpdateDropDownWidth();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateDropDownWidth();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDropDownWidth();
    }

    private void UpdateDropDownWidth()
    {
        var width = DropDownWidth;
        if (double.IsNaN(width) || width <= 0)
        {
            width = Button.ActualWidth;
        }

        if (width <= 0)
        {
            return;
        }

        ItemsList.MinWidth = width;
        ItemsList.MaxWidth = width;
    }

    private void UpdateContent()
    {
        var template = ItemTemplate;
        if (template is not null)
        {
            ContentText.Text = string.Empty;
            ContentText.Visibility = Visibility.Collapsed;
            TemplateContent.Content = SelectedItem;
            TemplateContent.ContentTemplate = template;
            TemplateContent.Visibility = Visibility.Visible;
            return;
        }

        TemplateContent.Visibility = Visibility.Collapsed;
        ContentText.Visibility = Visibility.Visible;
        var text = SelectedItem is null
            ? string.Empty
            : GetItemDisplayText(SelectedItem);
        ContentText.Text = string.IsNullOrEmpty(text) ? PlaceholderText ?? string.Empty : text;
    }

    private void SyncListSelection()
    {
        if (_isSyncing)
        {
            return;
        }

        _isSyncing = true;
        try
        {
            ItemsList.SelectedItem = SelectedItem;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnFlyoutOpening(object sender, object e)
    {
        SyncListSelection();
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is null)
        {
            return;
        }

        SelectedItem = e.ClickedItem;
        CloseDropDown();
        // 点击（以及 UI 自动化 invoke）只改变 ListView 的选中项，
        // ListView 自身的 SelectionChanged 在“选中项没变”时不会触发，
        // 于是外层拿不到通知、绑定值不会写回。这里补一次通知，
        // 重复通知由 RaiseSelectionChanged 去重。
        RaiseSelectionChanged(e.ClickedItem);
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing)
        {
            return;
        }

        var selected = ItemsList.SelectedItem;
        if (selected is null)
        {
            return;
        }

        if (!Equals(selected, SelectedItem))
        {
            SelectedItem = selected;
        }

        RaiseSelectionChanged(selected);
    }

    /// <summary>通知外层「选中项变了」，同一项只通知一次。</summary>
    private void RaiseSelectionChanged(object item)
    {
        if (_isSyncing)
        {
            return;
        }

        if (Equals(_lastNotifiedItem, item))
        {
            return;
        }

        _lastNotifiedItem = item;
        SelectionChanged?.Invoke(this, new PdxDropDownSelectionChangedEventArgs(item));
    }
}

/// <summary>下拉选择变化事件参数。</summary>
public sealed class PdxDropDownSelectionChangedEventArgs : EventArgs
{
    public PdxDropDownSelectionChangedEventArgs(object selectedItem)
    {
        SelectedItem = selectedItem;
    }

    public object SelectedItem { get; }
}
