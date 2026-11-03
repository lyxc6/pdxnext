namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;

/// <summary>上传面板里的一行 YouTube 视频预览。</summary>
public sealed class ModUploadVideoItem : LocalizedObservableObject
{
    /// <summary>构造；调用方必须已经用 <see cref="PdxLauncher.Core.Steam.YouTubeVideoId"/> 校验过。</summary>
    public ModUploadVideoItem(string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        VideoId = videoId;
    }

    /// <summary>11 位的 YouTube 视频 id。</summary>
    public string VideoId { get; }

    /// <summary>给人看的地址，也用作列表显示文本。</summary>
    public string Url => "https://www.youtube.com/watch?v=" + VideoId;
}
