namespace PdxLauncher.App.Controls;

using Microsoft.UI.Text;
using Windows.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PdxLauncher.Core.Text;

/// <summary>
/// 把 <see cref="MarkdownDocument"/> 渲染成富文本：标题分级、段落、列表、引用、代码块、
/// 粗体/斜体、链接（点击交给外部打开）、行内代码。
/// <para>
/// 为什么不用 RichTextBlock 手拼 Inlines：它对块级缩进/边框支持很弱，列表和引用都得自己算；
/// 这里直接用 StackPanel 拼 TextBlock，层级清晰、主题前景色自动继承。
/// </para>
/// <para>行内图片不内嵌（会撑破排版），统一收集到正文下方的图片墙。</para>
/// </summary>
public sealed partial class MarkdownView : ContentControl
{
    /// <summary>链接被点击（参数是 URL）。</summary>
    public event EventHandler<string>? LinkRequested;

    /// <summary>行内图片被点击（参数是图片 URL）。</summary>
    public event EventHandler<string>? ImageRequested;

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
        var host = new StackPanel { Spacing = 2 };
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
                return BuildTextBlock(
                    heading.Inlines,
                    HeadingSize(heading.Level),
                    FontWeights.SemiBold,
                    new Thickness(0, heading.Level == 1 ? 14 : 10, 0, 2));

            case MarkdownParagraph paragraph:
                return BuildTextBlock(paragraph.Inlines, 14, FontWeights.Normal, new Thickness(0, 2, 0, 2));

            case MarkdownListItem item:
            {
                var marker = item.Ordered
                    ? item.Number.ToString(System.Globalization.CultureInfo.CurrentCulture) + ". "
                    : "• ";
                var inlines = new List<MarkdownInline> { new MarkdownTextInline(marker) };
                inlines.AddRange(item.Inlines);

                var text = BuildTextBlock(inlines, 14, FontWeights.Normal, new Thickness(0, 1, 0, 1));
                text.Margin = new Thickness(16 + (item.Level * 20), 1, 0, 1);
                return text;
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
                    Margin = new Thickness(2),
                };

                return new Border
                {
                    Margin = new Thickness(0, 6, 0, 6),
                    Padding = new Thickness(10, 8, 10, 8),
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
                var inner = new StackPanel { Spacing = 2 };
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
                    Margin = new Thickness(0, 4, 0, 6),
                    Padding = new Thickness(10, 2, 0, 2),
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

    private TextBlock BuildTextBlock(
        IReadOnlyList<MarkdownInline> inlines,
        double size,
        FontWeight weight,
        Thickness margin)
    {
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
