namespace PdxLauncher.App.Controls;

using Microsoft.UI.Text;
using Windows.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Text;

/// <summary>
/// 把 <see cref="MarkdownDocument"/> 渲染成富文本：标题分级、段落、列表、引用、代码块、
/// 粗体/斜体、链接（点击交给外部打开）、行内代码，以及行内图片。
/// <para>
/// 为什么不用 RichTextBlock 手拼 Inlines：它对块级缩进/边框支持很弱，列表和引用都得自己算；
/// 这里直接用 StackPanel 拼 TextBlock，层级清晰、主题前景色自动继承。
/// </para>
/// <para>
/// 行内图片按「文字段 / 图片块」分段渲染：图片单独成块（受 MaxWidth/MaxHeight 限制），
/// 避免把排版撑破；本地文件由 <see cref="ImageResolver"/> 提供，未就绪先显示占位图标，
/// 下载完成后调 <see cref="RefreshImages"/> 原地换图，不重建整页。
/// </para>
/// </summary>
public sealed partial class MarkdownView : ContentControl
{
    /// <summary>已渲染的图片元素，key 是图片 URL（用于下载完成后原地刷新）。</summary>
    private readonly Dictionary<string, ImageSlot> _imageSlots = new(StringComparer.Ordinal);

    /// <summary>已渲染的评论表情（无扩展名的小图，下载完成后原地换成图片）。</summary>
    private readonly List<EmoticonSlot> _emoticonSlots = [];

    /// <summary>链接被点击（参数是 URL）。</summary>
    public event EventHandler<string>? LinkRequested;

    /// <summary>行内图片被点击（参数是图片 URL）。</summary>
    public event EventHandler<string>? ImageRequested;

    /// <summary>
    /// 图片 URL → 本地文件路径的解析器（未下载/下载失败返回 null）。
    /// 由页面接到视图模型上，本控件不碰网络。
    /// </summary>
    public Func<string, string?>? ImageResolver { get; set; }

    /// <summary>重新按 <see cref="ImageResolver"/> 取一遍本地图片（下载完成后调用）。</summary>
    public void RefreshImages()
    {
        foreach (var pair in _imageSlots)
        {
            var path = ImageResolver?.Invoke(pair.Key);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            pair.Value.Show(path);
        }

        foreach (var slot in _emoticonSlots)
        {
            var path = ImageResolver?.Invoke(slot.Url);
            if (!string.IsNullOrWhiteSpace(path))
            {
                slot.Show(path);
            }
        }
    }

    /// <summary>正文文档。</summary>
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(MarkdownDocument),
        typeof(MarkdownView),
        new PropertyMetadata(null, OnMarkdownChanged));

    /// <summary>正文文档。</summary>
    public MarkdownDocument? Markdown
    {
        get => (MarkdownDocument?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        (sender as MarkdownView)?.Rebuild();
    }

    private void Rebuild()
    {
        _imageSlots.Clear();
        _emoticonSlots.Clear();

        var host = new StackPanel { Spacing = 4 };
        var document = Markdown;

        if (document is not null && !document.IsEmpty)
        {
            foreach (var block in document.Blocks)
            {
                var element = BuildBlock(block, 0);
                if (element is not null)
                {
                    host.Children.Add(element);
                }
            }
        }

        Content = host;
    }

    private FrameworkElement? BuildBlock(MarkdownBlock block, int depth)
    {
        switch (block)
        {
            case MarkdownHeading heading:
                return BuildFlow(
                    heading.Inlines,
                    HeadingSize(heading.Level),
                    FontWeights.SemiBold,
                    new Thickness(0, heading.Level == 1 ? 16 : 12, 0, 4));

            case MarkdownParagraph paragraph:
                return BuildFlow(paragraph.Inlines, 14, FontWeights.Normal, new Thickness(0, 4, 0, 4));

            case MarkdownListItem item:
            {
                var marker = item.Ordered
                    ? item.Number.ToString(System.Globalization.CultureInfo.CurrentCulture) + ". "
                    : "• ";
                var inlines = new List<MarkdownInline> { new MarkdownTextInline(marker) };
                inlines.AddRange(item.Inlines);

                var element = BuildFlow(inlines, 14, FontWeights.Normal, new Thickness(0, 4, 0, 4));
                element.Margin = new Thickness(16 + (item.Level * 20), 0, 0, 0);
                return element;
            }

            case MarkdownCodeBlock code:
            {
                var text = new TextBlock
                {
                    Text = code.Text,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(4),
                };

                return new Border
                {
                    Margin = new Thickness(0, 8, 0, 8),
                    Padding = new Thickness(12, 8, 12, 8),
                    CornerRadius = new CornerRadius(4),
                    Background = LookupBrush("SubtleFillColorSecondaryBrush"),
                    Child = new ScrollViewer
                    {
                        HorizontalScrollMode = ScrollMode.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollMode = ScrollMode.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = text,
                    },
                };
            }

            case MarkdownQuote quote:
            {
                var inner = new StackPanel { Spacing = 4 };
                if (depth < 4)
                {
                    foreach (var child in quote.Blocks)
                    {
                        var element = BuildBlock(child, depth + 1);
                        if (element is not null)
                        {
                            inner.Children.Add(element);
                        }
                    }
                }

                return new Border
                {
                    Margin = new Thickness(0, 4, 0, 8),
                    Padding = new Thickness(12, 4, 0, 4),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = LookupBrush("CardStrokeColorDefaultBrush"),
                    Child = inner,
                };
            }

            case MarkdownRule:
                return new Rectangle
                {
                    Height = 1,
                    Margin = new Thickness(0, 8, 0, 8),
                    Fill = LookupBrush("CardStrokeColorDefaultBrush"),
                };

            default:
                return null;
        }
    }

    private static double HeadingSize(int level) => level switch
    {
        1 => 22,
        2 => 19,
        3 => 17,
        4 => 15.5,
        5 => 14.5,
        _ => 14,
    };

    private FrameworkElement BuildTextBlock(
        IReadOnlyList<MarkdownInline> inlines,
        double size,
        FontWeight weight,
        Thickness margin)
    {
        // 表情要作为 20×20 的 UI 元素嵌在文字流里，而 TextBlock 不能承载
        // InlineUIContainer（WinUI 只支持 RichTextBlock + Paragraph）。
        if (ContainsEmoticon(inlines))
        {
            var rich = new RichTextBlock
            {
                FontSize = size,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Margin = margin,
            };

            var paragraph = new Paragraph();
            AppendInlines(paragraph.Inlines, inlines, weight, FontStyle.Normal);
            rich.Blocks.Add(paragraph);
            return rich;
        }

        var text = new TextBlock
        {
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = margin,
        };

        AppendInlines(text.Inlines, inlines, weight, FontStyle.Normal);
        return text;
    }

    /// <summary>这段行内流里是否至少有一个可渲染的官方表情。</summary>
    private static bool ContainsEmoticon(IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownImageInline image when SteamEmoticon.IsEmoticonImage(image.Url, image.Alt):
                    return true;

                case MarkdownLinkInline link when link.Inlines.Count == 1
                    && ContainsEmoticon(link.Inlines):
                    return true;

                case MarkdownStyledInline styled when styled.Inlines.Count == 1
                    && ContainsEmoticon(styled.Inlines):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 渲染一段行内流：没有图片时就是一段 TextBlock；有图片时按「文字段 / 图片块」分段，
    /// 图片单独成块，避免被排版挤成一条线。
    /// </summary>
    private FrameworkElement BuildFlow(
        IReadOnlyList<MarkdownInline> inlines,
        double size,
        FontWeight weight,
        Thickness margin)
    {
        if (!HasImage(inlines))
        {
            return BuildTextBlock(inlines, size, weight, margin);
        }

        var panel = new StackPanel { Spacing = 4, Margin = margin };
        var buffer = new List<MarkdownInline>();

        void FlushText()
        {
            if (buffer.Count == 0)
            {
                return;
            }

            var text = BuildTextBlock(buffer, size, weight, new Thickness(0));
            panel.Children.Add(text);
            buffer.Clear();
        }

        foreach (var inline in inlines)
        {
            if (TryGetImage(inline, out var image) && image is not null && !IsInlineImage(image))
            {
                FlushText();
                panel.Children.Add(BuildImage(image.Url));
                continue;
            }

            // 图片前后常有一串 <br>，段落开头/结尾的换行不单独成行。
            if (inline is MarkdownBreakInline && buffer.Count == 0)
            {
                continue;
            }

            buffer.Add(inline);
        }

        FlushText();
        return panel;
    }

    /// <summary>
    /// 是否还有「需要单独成块」的图片。官方表情走行内小图，不参与分块；
    /// alt 是 <c>:name:</c> 的非表情图也按短码行内文本处理，不再占 240×160 的灰块。
    /// </summary>
    private static bool HasImage(IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (TryGetImage(inline, out var image) && image is not null && !IsInlineImage(image))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>图片是否只该以行内形式出现（官方表情，或 alt 本身就是短码的失效表情）。</summary>
    private static bool IsInlineImage(MarkdownImageInline image) =>
        SteamEmoticon.IsEmoticonImage(image.Url, image.Alt)
        || SteamEmoticon.TryGetShortCode(image.Alt, out _);

    /// <summary>
    /// 取出一段行内里「唯一」的图片：直接是图片、或者整段被包在链接/样式里（Steam 有 <c>&lt;a&gt;&lt;img&gt;&lt;/a&gt;</c>）。
    /// </summary>
    private static bool TryGetImage(MarkdownInline inline, out MarkdownImageInline? image)
    {
        switch (inline)
        {
            case MarkdownImageInline direct when !string.IsNullOrWhiteSpace(direct.Url):
                image = direct;
                return true;

            case MarkdownLinkInline link when link.Inlines.Count == 1:
                return TryGetImage(link.Inlines[0], out image);

            case MarkdownStyledInline styled when styled.Inlines.Count == 1:
                return TryGetImage(styled.Inlines[0], out image);

            default:
                image = null;
                return false;
        }
    }

    /// <summary>单张正文配图：圆角容器 + 占位图标 + 实际图片，点击进全屏查看器。</summary>
    private FrameworkElement BuildImage(string url)
    {
        var picture = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 640,
            MaxHeight = 420,
            Visibility = Visibility.Collapsed,
        };

        var placeholder = new FontIcon
        {
            Glyph = "\uE8B9",
            FontSize = 28,
            Foreground = LookupBrush("TextFillColorSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 240,
            MinHeight = 160,
            Padding = new Thickness(8),
        };
        host.Children.Add(placeholder);
        host.Children.Add(picture);

        var border = new Border
        {
            Margin = new Thickness(0, 4, 0, 4),
            CornerRadius = new CornerRadius(8),
            Background = LookupBrush("SubtleFillColorSecondaryBrush"),
            Child = host,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var label = Localizer.Instance.T(Keys.WorkshopDetailViewImage);
        ToolTipService.SetToolTip(border, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(border, label);
        border.Tapped += (_, args) =>
        {
            args.Handled = true;
            ImageRequested?.Invoke(this, url);
        };

        var slot = new ImageSlot(picture, placeholder);
        _imageSlots[url] = slot;

        if (ImageResolver?.Invoke(url) is { Length: > 0 } localPath)
        {
            slot.Show(localPath);
        }

        return border;
    }

    /// <summary>一张正文配图的宿主元素（图片 + 占位图标）。</summary>
    private sealed class ImageSlot(Image picture, FontIcon placeholder)
    {
        /// <summary>切到本地文件显示。</summary>
        public void Show(string path)
        {
            var source = XamlImage.FromPath(path, 1024);
            if (source is null)
            {
                return;
            }

            if (ReferenceEquals(picture.Source, source))
            {
                return;
            }

            picture.Source = source;
            picture.Visibility = Visibility.Visible;
            placeholder.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>行内表情：小图 + 短码兜底，URL 用于刷新时重新解析本地路径。</summary>
    private sealed class EmoticonSlot(Image picture, TextBlock fallback, string url)
    {
        /// <summary>表情 URL（已规范化，无 query/fragment）。</summary>
        public string Url { get; } = url;

        /// <summary>切到本地文件显示；失败则继续显示短码。</summary>
        public void Show(string path)
        {
            var source = XamlImage.FromPath(path, 20);
            if (source is null)
            {
                return;
            }

            if (ReferenceEquals(picture.Source, source))
            {
                return;
            }

            picture.Source = source;
            picture.Visibility = Visibility.Visible;
            fallback.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>把一个官方表情作为 20×20 行内元素塞进文字流；本地图未就绪时显示短码。</summary>
    private void AddEmoticon(InlineCollection target, string url, string shortCode)
    {
        var fallback = new TextBlock
        {
            Text = shortCode,
            FontSize = 14,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var picture = new Image
        {
            Width = 20,
            Height = 20,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        var host = new Grid { Height = 20, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(picture);
        host.Children.Add(fallback);

        target.Add(new InlineUIContainer { Child = host });

        var slot = new EmoticonSlot(picture, fallback, url);
        _emoticonSlots.Add(slot);
        if (ImageResolver?.Invoke(url) is { Length: > 0 } path)
        {
            slot.Show(path);
        }
    }

    private void AppendInlines(
        InlineCollection target,
        IReadOnlyList<MarkdownInline> inlines,
        FontWeight weight,
        FontStyle style)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text:
                    target.Add(new Run { Text = text.Text, FontWeight = weight, FontStyle = style });
                    break;

                case MarkdownCodeInline code:
                    target.Add(new Run
                    {
                        Text = code.Text,
                        FontFamily = new FontFamily("Consolas"),
                        FontWeight = weight,
                        FontStyle = style,
                    });
                    break;

                case MarkdownBreakInline:
                    target.Add(new LineBreak());
                    break;

                case MarkdownStyledInline styled:
                {
                    var styledWeight = styled.Style == MarkdownInlineStyle.Bold ? FontWeights.SemiBold : weight;
                    var styledFont = styled.Style == MarkdownInlineStyle.Italic ? FontStyle.Italic : style;
                    AppendInlines(target, styled.Inlines, styledWeight, styledFont);
                    break;
                }

                case MarkdownLinkInline link:
                {
                    var hyperlink = new Hyperlink();
                    foreach (var child in link.Inlines)
                    {
                        switch (child)
                        {
                            case MarkdownTextInline text:
                                hyperlink.Inlines.Add(new Run { Text = text.Text });
                                break;
                            case MarkdownCodeInline code:
                                hyperlink.Inlines.Add(new Run
                                {
                                    Text = code.Text,
                                    FontFamily = new FontFamily("Consolas"),
                                });
                                break;
                            case MarkdownBreakInline:
                                hyperlink.Inlines.Add(new LineBreak());
                                break;
                        }
                    }

                    if (hyperlink.Inlines.Count == 0)
                    {
                        hyperlink.Inlines.Add(new Run { Text = link.Url });
                    }

                    var url = link.Url;
                    hyperlink.Click += (_, _) => LinkRequested?.Invoke(this, url);
                    target.Add(hyperlink);
                    break;
                }

                case MarkdownImageInline image
                    when SteamEmoticon.TryCreate(image.Url, image.Alt, out var emoticonUrl, out var shortCode):
                    AddEmoticon(target, emoticonUrl, shortCode);
                    break;

                case MarkdownImageInline image when SteamEmoticon.TryGetShortCode(image.Alt, out var fallbackCode):
                    target.Add(new Run { Text = fallbackCode });
                    break;

                case MarkdownImageInline image:
                {
                    var hyperlink = new Hyperlink();
                    hyperlink.Inlines.Add(new Run { Text = "［图片］" });
                    var url = image.Url;
                    hyperlink.Click += (_, _) => ImageRequested?.Invoke(this, url);
                    target.Add(hyperlink);
                    break;
                }
            }
        }
    }

    private static Brush? LookupBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
