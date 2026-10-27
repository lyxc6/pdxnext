using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace PdxLauncher.App.Controls;

/// <summary>
/// 单行设置卡片：左侧标题（可选说明），右侧操作控件。
/// 参考 Windows 设置的 SettingsCard 布局，但完全本地实现（离线构建取不到官方控件包）。
/// </summary>
public sealed partial class SettingsCard : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(SettingsCard), new PropertyMetadata(null));

    public SettingsCard()
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

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public Visibility DescriptionVisibility =>
        string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (SettingsCard)sender;
        // WinUI 的 DependencyPropertyChangedEventArgs 不暴露属性名（WPF 才有），因此统一刷新三个计算属性。
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(Header)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(Description)));
        card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(DescriptionVisibility)));
    }
}
