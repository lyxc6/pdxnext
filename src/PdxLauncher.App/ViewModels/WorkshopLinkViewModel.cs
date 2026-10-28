namespace PdxLauncher.App.ViewModels;

/// <summary>
/// 工坊详情里的一条外部链接（GitHub / YouTube / 论坛帖等）。
/// 服务端的 <c>displayName</c> 经常为 null，标题由 <see cref="WorkshopDetailViewModel"/> 决定怎么兜底。
/// </summary>
public sealed class WorkshopLinkViewModel
{
    /// <summary>构造。</summary>
    /// <param name="title">显示文案。</param>
    /// <param name="url">绝对地址。</param>
    /// <param name="type">服务端给的类型标签（github / youtube / kofi…），可能为空。</param>
    /// <param name="index">在链接列表里的序号。</param>
    public WorkshopLinkViewModel(string title, string url, string type, int index)
    {
        Title = title;
        Url = url;
        Type = type;
        Index = index;
    }

    /// <summary>显示文案。</summary>
    public string Title { get; }

    /// <summary>绝对地址。</summary>
    public string Url { get; }

    /// <summary>服务端类型标签，可能为空。</summary>
    public string Type { get; }

    /// <summary>序号（0 基）。</summary>
    public int Index { get; }

    /// <summary>自动化测试用的标识。</summary>
    public string AutomationId => "workshopDetailLink_" + Index;
}
