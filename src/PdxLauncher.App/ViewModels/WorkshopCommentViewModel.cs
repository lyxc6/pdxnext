namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Text;

/// <summary>Steam 评论区的一条（mod 详情页「讨论」卡）。</summary>
public sealed class WorkshopCommentViewModel : LocalizedObservableObject
{
    private MarkdownDocument? _translatedBody;
    private string? _translatedBodyPlain;
    /// <summary>构造。</summary>
    /// <param name="index">行号。</param>
    /// <param name="authorName">作者名。</param>
    /// <param name="authorProfileUrl">作者主页（可能为 null）。</param>
    /// <param name="postedUtc">发表时间（未知时为 <see cref="DateTimeOffset.UnixEpoch"/>）。</param>
    /// <param name="body">正文 AST。</param>
    /// <param name="bodyPlain">正文纯文本（无障碍/兜底）。</param>
    public WorkshopCommentViewModel(
        int index,
        string authorName,
        string? authorProfileUrl,
        DateTimeOffset postedUtc,
        MarkdownDocument body,
        string bodyPlain)
    {
        Index = index;
        AuthorName = authorName;
        AuthorProfileUrl = authorProfileUrl;
        Body = body ?? MarkdownDocument.Empty;
        BodyPlain = bodyPlain ?? string.Empty;
        PostedText = WorkshopText.FormatDate(postedUtc);
        AutomationId = "workshopComment_" + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>行号。</summary>
    public int Index { get; }

    /// <summary>作者显示名。</summary>
    public string AuthorName { get; }

    /// <summary>作者主页（点击跳浏览器）；null 表示没有。</summary>
    public string? AuthorProfileUrl { get; }

    /// <summary>作者主页是否可用。</summary>
    public bool HasProfile => !string.IsNullOrWhiteSpace(AuthorProfileUrl);

    /// <summary>发表时间文案（可能为空串）。</summary>
    public string PostedText { get; }

    /// <summary>是否有时间。</summary>
    public bool HasPosted => !string.IsNullOrWhiteSpace(PostedText);

    /// <summary>正文。</summary>
    public MarkdownDocument Body { get; }

    /// <summary>正文纯文本。</summary>
    public string BodyPlain { get; }

    /// <summary>正文是否为空。</summary>
    public bool HasBody => !Body.IsEmpty;

    /// <summary>自动化 id。</summary>
    public string AutomationId { get; }

    /// <summary>作者按钮的自动化 id。</summary>
    public string AuthorAutomationId => AutomationId + "_author";

    /// <summary>译文正文；没有启用翻译、翻译失败或原文已是目标语言时为 null。</summary>
    public MarkdownDocument? TranslatedBody
    {
        get => _translatedBody;
        set
        {
            if (ReferenceEquals(_translatedBody, value))
            {
                return;
            }

            _translatedBody = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTranslation));
        }
    }

    /// <summary>译文纯文本（无障碍名称用）。</summary>
    public string? TranslatedBodyPlain
    {
        get => _translatedBodyPlain;
        set
        {
            if (string.Equals(_translatedBodyPlain, value, StringComparison.Ordinal))
            {
                return;
            }

            _translatedBodyPlain = value;
            OnPropertyChanged();
        }
    }

    /// <summary>是否有可显示的译文。</summary>
    public bool HasTranslation => _translatedBody is { IsEmpty: false };
}
