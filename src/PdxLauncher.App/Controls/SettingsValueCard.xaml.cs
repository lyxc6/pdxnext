using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace PdxLauncher.App.Controls;

/// <summary>
/// 只读值卡片：左侧标题（可选说明），右侧一段会截断的长文本。
/// 设置页里十来处同构卡片（路径、时间、统计数字）都走这里，页面只写绑定。
/// </summary>
public sealed partial class SettingsValueCard : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsValueCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsValueCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(SettingsValueCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty ValueAutomationIdProperty = DependencyProperty.Register(
        nameof(ValueAutomationId), typeof(string), typeof(SettingsValueCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty ValueAutomationNameProperty = DependencyProperty.Register(
        nameof(ValueAutomationName), typeof(string), typeof(SettingsValueCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty MaxValueWidthProperty = DependencyProperty.Register(
        nameof(MaxValueWidth), typeof(double), typeof(SettingsValueCard), new PropertyMetadata(420d, OnContentChanged));

    public static readonly DependencyProperty ShowToolTipProperty = DependencyProperty.Register(
        nameof(ShowToolTip), typeof(bool), typeof(SettingsValueCard), new PropertyMetadata(true, OnContentChanged));

    public SettingsValueCard()
    {
        InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string ValueAutomationId
    {
        get => (string)GetValue(ValueAutomationIdProperty);
        set => SetValue(ValueAutomationIdProperty, value);
    }

    public string ValueAutomationName
    {
        get => (string)GetValue(ValueAutomationNameProperty);
        set => SetValue(ValueAutomationNameProperty, value);
    }

    public double MaxValueWidth
    {
        get => (double)GetValue(MaxValueWidthProperty);
        set => SetValue(MaxValueWidthProperty, value);
    }

    public bool ShowToolTip
    {
        get => (bool)GetValue(ShowToolTipProperty);
        set => SetValue(ShowToolTipProperty, value);
    }

    /// <summary>值为空或显式关掉时返回 null —— 没有内容就不该有提示气泡。</summary>
    public string? ToolTipText => ShowToolTip && !string.IsNullOrEmpty(Value) ? Value : null;

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (SettingsValueCard)sender;
        // WinUI 的 DependencyPropertyChangedEventArgs 不暴露属性名（WPF 才有），因此统一刷新全部公开属性。
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(Header)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(Description)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(Value)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(ValueAutomationId)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(ValueAutomationName)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(MaxValueWidth)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(ShowToolTip)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(ToolTipText)));
    }
}
