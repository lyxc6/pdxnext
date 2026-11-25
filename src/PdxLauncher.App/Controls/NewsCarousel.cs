namespace PdxLauncher.App.Controls;

using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;

/// <summary>
/// 主页公告槽位轮播：对齐官方 2026.14 的 <c>.newsSlot</c> 行为。
/// <para>
/// 单卡时不显示箭头与进度点；多卡时 2 秒自动切换、悬停显示左右箭头、底部显示进度段。
/// 卡片点击只打开当前可见卡的链接，深链仍交由页面弹页内提示。
/// </para>
/// </summary>
public sealed class NewsCarousel : Control
{
    /// <summary>官方 slick 轮播的自动切换间隔（2 秒）。</summary>
    public static TimeSpan AutoAdvanceInterval => NewsCarouselPolicy.AutoAdvanceInterval;

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items),
        typeof(object),
        typeof(NewsCarousel),
        new PropertyMetadata(null, OnItemsChanged));

    public static readonly DependencyProperty ImageDecodeWidthProperty = DependencyProperty.Register(
        nameof(ImageDecodeWidth),
        typeof(double),
        typeof(NewsCarousel),
        new PropertyMetadata(1024d, OnImageDecodeWidthChanged));

    public static readonly DependencyProperty CardAutomationIdProperty = DependencyProperty.Register(
        nameof(CardAutomationId),
        typeof(string),
        typeof(NewsCarousel),
        new PropertyMetadata(string.Empty, OnCardAutomationIdChanged));

    public static readonly DependencyProperty CardAutomationNameProperty = DependencyProperty.Register(
        nameof(CardAutomationName),
        typeof(string),
        typeof(NewsCarousel),
        new PropertyMetadata(string.Empty, OnCardAutomationNameChanged));

    public static readonly DependencyProperty PreviousAutomationNameProperty = DependencyProperty.Register(
        nameof(PreviousAutomationName),
        typeof(string),
        typeof(NewsCarousel),
        new PropertyMetadata(string.Empty, OnPreviousAutomationNameChanged));

    public static readonly DependencyProperty NextAutomationNameProperty = DependencyProperty.Register(
        nameof(NextAutomationName),
        typeof(string),
        typeof(NewsCarousel),
        new PropertyMetadata(string.Empty, OnNextAutomationNameChanged));

    public static readonly DependencyProperty PreviousToolTipProperty = DependencyProperty.Register(
        nameof(PreviousToolTip),
        typeof(string),
        typeof(NewsCarousel),
        new PropertyMetadata(string.Empty, OnPreviousToolTipChanged));

    public static readonly DependencyProperty NextToolTipProperty = DependencyProperty.Register(
        nameof(NextToolTip),
        typeof(string),
        typeof(NewsCarousel),
        new PropertyMetadata(string.Empty, OnNextToolTipChanged));

    private readonly DispatcherTimer _timer = new() { Interval = AutoAdvanceInterval };
    private Button? _cardButton;
    private Button? _previousButton;
    private Button? _nextButton;
    private Image? _cardImage;
    private Border? _titleScrim;
    private TextBlock? _cardTitle;
    private Border? _hoverVeil;
    private StackPanel? _dots;
    private Grid? _root;
    private int _index;
    private bool _hovered;

    public NewsCarousel()
    {
        DefaultStyleKey = typeof(NewsCarousel);
        _timer.Tick += OnTimerTick;
        Unloaded += OnCarouselUnloaded;
    }

    /// <summary>
    /// 当前槽位的公告卡（同一 slot 可有多张）。类型写成 object 是为了让 WinUI 只做集合绑定，
    /// 不把 NewsCardItem 当成可写 XAML 类型生成属性 setter（它自身是不可变的投影对象）。
    /// </summary>
    public object Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>图片解码宽度；main 用 1024，secondary 用 640。</summary>
    public double ImageDecodeWidth
    {
        get => (double)GetValue(ImageDecodeWidthProperty);
        set => SetValue(ImageDecodeWidthProperty, value);
    }

    public string CardAutomationId
    {
        get => (string)GetValue(CardAutomationIdProperty);
        set => SetValue(CardAutomationIdProperty, value);
    }

    public string CardAutomationName
    {
        get => (string)GetValue(CardAutomationNameProperty);
        set => SetValue(CardAutomationNameProperty, value);
    }

    public string PreviousAutomationName
    {
        get => (string)GetValue(PreviousAutomationNameProperty);
        set => SetValue(PreviousAutomationNameProperty, value);
    }

    public string NextAutomationName
    {
        get => (string)GetValue(NextAutomationNameProperty);
        set => SetValue(NextAutomationNameProperty, value);
    }

    public string PreviousToolTip
    {
        get => (string)GetValue(PreviousToolTipProperty);
        set => SetValue(PreviousToolTipProperty, value);
    }

    public string NextToolTip
    {
        get => (string)GetValue(NextToolTipProperty);
        set => SetValue(NextToolTipProperty, value);
    }

    /// <summary>当前可见卡；没有卡时为 null。</summary>
    public NewsCardItem? CurrentItem
    {
        get
        {
            var cards = Cards;
            return cards.Count == 0 ? null : cards[Math.Clamp(_index, 0, cards.Count - 1)];
        }
    }

    /// <summary>把绑定值收敛成卡片列表：IList 直接读，其它当作空集合。</summary>
    private IList<NewsCardItem> Cards => Items is IList<NewsCardItem> cards ? cards : Array.Empty<NewsCardItem>();

    /// <summary>点开当前卡：http(s) 交给 shell，深链由页面提示。</summary>
    public event EventHandler<NewsCardItem>? CardActivated;

    protected override void OnApplyTemplate()
    {
        DetachTemplate();
        base.OnApplyTemplate();

        _root = GetTemplateChild("Root") as Grid;
        _cardButton = GetTemplateChild("CardButton") as Button;
        _previousButton = GetTemplateChild("PreviousButton") as Button;
        _nextButton = GetTemplateChild("NextButton") as Button;
        _cardImage = GetTemplateChild("CardImage") as Image;
        _titleScrim = GetTemplateChild("TitleScrim") as Border;
        _cardTitle = GetTemplateChild("CardTitle") as TextBlock;
        _hoverVeil = GetTemplateChild("HoverVeil") as Border;
        _dots = GetTemplateChild("DotsPanel") as StackPanel;

        if (_root is not null)
        {
            _root.PointerEntered += OnRootPointerEntered;
            _root.PointerExited += OnRootPointerExited;
            _root.PointerMoved += OnRootPointerMoved;
        }
        else if (_cardButton is not null)
        {
            _cardButton.PointerEntered += OnRootPointerEntered;
            _cardButton.PointerExited += OnRootPointerExited;
        }

        if (_cardButton is not null)
        {
            _cardButton.Click += OnCardButtonClick;
        }

        if (_previousButton is not null)
        {
            _previousButton.Click += OnPreviousButtonClick;
            AutomationProperties.SetName(_previousButton, PreviousAutomationName);
            ToolTipService.SetToolTip(_previousButton, PreviousToolTip);
        }

        if (_nextButton is not null)
        {
            _nextButton.Click += OnNextButtonClick;
            AutomationProperties.SetName(_nextButton, NextAutomationName);
            ToolTipService.SetToolTip(_nextButton, NextToolTip);
        }

        ApplyAutomationIds();
        Render();
    }

    private void OnCarouselUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        HookCollection(Items as INotifyCollectionChanged, null);
        DetachTemplate();
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (NewsCarousel)d;
        control.HookCollection(e.OldValue as INotifyCollectionChanged, e.NewValue as INotifyCollectionChanged);
        control._index = 0;
        control.Render();
        control.RestartTimer();
    }

    private static void OnImageDecodeWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NewsCarousel)d).Render();

    private static void OnCardAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NewsCarousel)d).ApplyAutomationIds();

    private static void OnCardAutomationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NewsCarousel)d).ApplyAutomationIds();

    private static void OnPreviousAutomationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (NewsCarousel)d;
        if (control._previousButton is not null)
        {
            AutomationProperties.SetName(control._previousButton, control.PreviousAutomationName);
        }
    }

    private static void OnNextAutomationNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (NewsCarousel)d;
        if (control._nextButton is not null)
        {
            AutomationProperties.SetName(control._nextButton, control.NextAutomationName);
        }
    }

    private static void OnPreviousToolTipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (NewsCarousel)d;
        if (control._previousButton is not null)
        {
            ToolTipService.SetToolTip(control._previousButton, control.PreviousToolTip);
        }
    }

    private static void OnNextToolTipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (NewsCarousel)d;
        if (control._nextButton is not null)
        {
            ToolTipService.SetToolTip(control._nextButton, control.NextToolTip);
        }
    }

    private void ApplyAutomationIds()
    {
        if (_cardButton is not null)
        {
            AutomationProperties.SetAutomationId(_cardButton, CardAutomationId);
            AutomationProperties.SetName(_cardButton, CardAutomationName);
        }

        if (_cardImage is not null)
        {
            AutomationProperties.SetName(_cardImage, CardAutomationName);
        }
    }

    private void Render()
    {
        var items = Cards;
        var hasItems = items.Count > 0;
        var current = CurrentItem;

        if (_cardButton is not null)
        {
            _cardButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_cardImage is not null)
        {
            _cardImage.Source = current is null
                ? null
                : XamlImage.FromPath(current.ImagePath, ImageDecodeWidth);
            _cardImage.Visibility = current?.HasImage == true ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_titleScrim is not null)
        {
            _titleScrim.Visibility = current?.HasTitle == true ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_cardTitle is not null)
        {
            _cardTitle.Text = current?.Title ?? string.Empty;
            _cardTitle.Visibility = current?.HasTitle == true ? Visibility.Visible : Visibility.Collapsed;
        }

        var multi = NewsCarouselPolicy.ShouldShowDots(items.Count);
        if (_previousButton is not null)
        {
            _previousButton.Visibility = NewsCarouselPolicy.ShouldShowArrows(items.Count, _hovered)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_nextButton is not null)
        {
            _nextButton.Visibility = NewsCarouselPolicy.ShouldShowArrows(items.Count, _hovered)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_dots is not null)
        {
            _dots.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            RebuildDots(items.Count);
        }

        // 模板还没应用时 _cardButton 为 null；AutomationProperties.SetName(null, …) 会直接崩在 coreclr。
        if (current is not null && _cardButton is not null)
        {
            var name = current.ToString();
            AutomationProperties.SetName(_cardButton, name);
            AutomationProperties.SetHelpText(_cardButton, current.Slot);
            if (_cardImage is not null)
            {
                AutomationProperties.SetName(_cardImage, name);
            }
        }
    }

    private void RebuildDots(int count)
    {
        if (_dots is null)
        {
            return;
        }

        _dots.Children.Clear();
        for (var i = 0; i < count; i++)
        {
            var active = i == _index;
            _dots.Children.Add(new Rectangle
            {
                Width = 24,
                Height = 4,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(active ? Windows.UI.Color.FromArgb(0x99, 0xB9, 0xC0, 0xCB)
                                                   : Windows.UI.Color.FromArgb(0x4D, 0xB9, 0xC0, 0xCB)),
            });
        }
    }

    private void RestartTimer()
    {
        _timer.Stop();
        if (NewsCarouselPolicy.ShouldAutoAdvance(Cards.Count))
        {
            _timer.Start();
        }
    }

    private void OnTimerTick(object? sender, object e) =>
        Advance(1);

    private void OnPreviousButtonClick(object sender, RoutedEventArgs e) =>
        Advance(-1);

    private void OnNextButtonClick(object sender, RoutedEventArgs e) =>
        Advance(1);

    private void Advance(int delta)
    {
        var count = Cards.Count;
        _index = NewsCarouselPolicy.Advance(_index, count, delta);
        if (count <= 0)
        {
            return;
        }

        Render();
        RestartTimer();
    }

    private void OnCardButtonClick(object sender, RoutedEventArgs e)
    {
        if (CurrentItem is { } current)
        {
            CardActivated?.Invoke(this, current);
        }
    }

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;
        if (_hoverVeil is not null)
        {
            _hoverVeil.Opacity = 1;
        }

        Render();
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;
        if (_hoverVeil is not null)
        {
            _hoverVeil.Opacity = 0;
        }

        Render();
    }

    /// <summary>在取证区域里轻微移动：WinUI 的退出/进入合成可能漏发，这里按坐标兜底。</summary>
    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_root is null)
        {
            return;
        }

        _hovered = true;
        if (_hoverVeil is not null)
        {
            _hoverVeil.Opacity = 1;
        }

        var items = Cards.Count;
        if (NewsCarouselPolicy.ShouldShowArrows(items, _hovered))
        {
            if (_previousButton is not null)
            {
                _previousButton.Visibility = Visibility.Visible;
            }

            if (_nextButton is not null)
            {
                _nextButton.Visibility = Visibility.Visible;
            }
        }
    }

    private void HookCollection(INotifyCollectionChanged? oldValue, INotifyCollectionChanged? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.CollectionChanged -= OnCardsChanged;
        }

        if (newValue is not null)
        {
            newValue.CollectionChanged += OnCardsChanged;
        }
    }

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _index = 0;
        Render();
        RestartTimer();
    }

    private void DetachTemplate()
    {
        if (_cardButton is not null)
        {
            _cardButton.Click -= OnCardButtonClick;
            _cardButton.PointerEntered -= OnRootPointerEntered;
            _cardButton.PointerExited -= OnRootPointerExited;
        }

        if (_root is not null)
        {
            _root.PointerEntered -= OnRootPointerEntered;
            _root.PointerExited -= OnRootPointerExited;
            _root.PointerMoved -= OnRootPointerMoved;
        }

        if (_previousButton is not null)
        {
            _previousButton.Click -= OnPreviousButtonClick;
        }

        if (_nextButton is not null)
        {
            _nextButton.Click -= OnNextButtonClick;
        }
    }
}
