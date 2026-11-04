namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;

using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// 工坊封面/截图的 96px、256px 两档缩略图。
/// <para>
/// 下载回来的是 1900px 左右的原图（解码一次 14 MB），而列表里只画 48–72px。
/// 每次打开列表都从原图缩一遍是纯浪费，所以在下载完成时就顺手落两份小图，
/// 列表优先用 <c>xxx.96.png</c>，详情页优先用 <c>xxx.256.png</c>。
/// </para>
/// <para>
/// 只缩不放：原图本身比目标还小时不生成，避免小图被放大后更糊。
/// 生成失败只记 Warn，绝不影响原图显示；没有变体的老文件照旧按原图解码。
/// </para>
/// </summary>
public static class ThumbnailVariants
{
    /// <summary>列表缩略图那一档（48–72 DIP 在 150% 缩放下约 108px，96 足够）。</summary>
    public const int SmallWidth = 96;

    /// <summary>详情页小图那一档（200×120 DIP 在 150% 缩放下约 300px，256 足够）。</summary>
    public const int LargeWidth = 256;

    /// <summary>
    /// 按解码宽度挑一档变体宽度；解码宽度不在两档附近时返回 0（用原图）。
    /// </summary>
    public static int PickWidth(int decodeWidth)
    {
        if (decodeWidth <= 0)
        {
            return 0;
        }

        if (decodeWidth <= SmallWidth)
        {
            return SmallWidth;
        }

        return decodeWidth <= LargeWidth ? LargeWidth : 0;
    }

    /// <summary>变体文件路径：与原图同目录，文件名加 <c>.96</c> / <c>.256</c> 后缀。</summary>
    public static string PathFor(string path, int width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path) + "." + width.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png";
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
    }

    /// <summary>已存在的变体路径；没有就返回 null（调用方继续用原图）。</summary>
    public static string? Resolve(IFileSystem fileSystem, string path, int decodeWidth)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var width = PickWidth(decodeWidth);
        if (width == 0)
        {
            return null;
        }

        var variant = PathFor(path, width);
        return fileSystem.FileExists(variant) ? variant : null;
    }

    /// <summary>
    /// 为一张原图生成两档变体（已存在就跳过）。后台执行，失败只记 Warn。
    /// </summary>
    public static void GenerateInBackground(IFileSystem fileSystem, IAppLogger? logger, string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !fileSystem.FileExists(path))
        {
            return;
        }

        // 后台任务自带完整 try/catch：异常没人 await，不能让它把进程带崩。
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await GenerateAsync(fileSystem, path, SmallWidth, cancellationToken).ConfigureAwait(false);
                    await GenerateAsync(fileSystem, path, LargeWidth, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 页面已经关了，剩下的不用生成。
                }
                catch (Exception exception)
                {
                    // 缩略图是纯优化：失败只影响列表观感，不该刷屏。
                    // 异常消息里常带换行与 WinRT 的 HRESULT 后缀，压成一行再记。
                    logger?.Warn(
                        "缩略图变体生成失败（" + path + "）："
                        + exception.GetType().Name + ": " + Flatten(exception.Message));
                }
            },
            CancellationToken.None);
    }

    /// <summary>把异常消息里的换行/连续空白压成单个空格，日志一行读完。</summary>
    private static string Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task GenerateAsync(IFileSystem fileSystem, string path, int width, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = PathFor(path, width);
        if (fileSystem.FileExists(target))
        {
            return;
        }

        var source = await StorageFile.GetFileFromPathAsync(path);
        using var input = await source.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(input);

        if (decoder.PixelWidth <= width)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var height = Math.Max(1, (int)Math.Round(decoder.PixelHeight * (double)width / decoder.PixelWidth));
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform
            {
                ScaledWidth = (uint)width,
                ScaledHeight = (uint)height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var directory = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path));
        var file = await directory.CreateFileAsync(Path.GetFileName(target), CreationCollisionOption.ReplaceExisting);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
    }
}
