namespace PdxLauncher.App.ViewModels;

using System.IO;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Steam;

/// <summary>
/// 上传面板里的一行附加预览图。
/// <para>
/// 只是一层展示包装：真正的 1 MB 限制判定在 <see cref="ModPublishLimits"/> 里，
/// 这里只负责把结果翻成给人看的文案，避免 XAML 里堆 converter。
/// </para>
/// </summary>
public sealed class ModUploadPreviewItem : LocalizedObservableObject
{
    /// <summary>构造。</summary>
    public ModUploadPreviewItem(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Length = ReadLength(path);
        IsTooLarge = !ModPublishLimits.IsPreviewFileAcceptable(Length, out _);
    }

    /// <summary>完整路径。</summary>
    public string Path { get; }

    /// <summary>文件名，列表里只显示这个。</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>字节数；读不到时为 0。</summary>
    public long Length { get; }

    /// <summary>是否不能上传（空文件或 ≥1 MB）。</summary>
    public bool IsTooLarge { get; }

    /// <summary>体积文案。</summary>
    public string SizeText => ModSizeText.Format(Length);

    /// <summary>超限时的警告文案；正常时为空字符串。</summary>
    public string TooLargeText => IsTooLarge
        ? Localizer.Instance.T(Keys.ModstudioPreviewTooLarge)
        : string.Empty;

    /// <summary>是否显示超限警告。</summary>
    public bool HasTooLargeText => IsTooLarge;

    private static long ReadLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
