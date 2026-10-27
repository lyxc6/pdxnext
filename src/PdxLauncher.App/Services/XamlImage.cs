namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

/// <summary>
/// 图片路径（string?）→ ImageSource 的 XAML 辅助函数。
/// 返回的 BitmapImage 由框架在后台线程解码，不会阻塞 UI 线程；
/// 需要"先占位再替换"的场景请改用 <see cref="BitmapLoader"/>。
/// </summary>
public static class XamlImage
{
    /// <summary>路径为空、文件不存在或创建失败时返回 null（XAML 侧显示占位）。</summary>
    public static ImageSource? FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!AppServices.IsInitialized || !AppServices.FileSystem.FileExists(path))
            {
                return null;
            }

            return new BitmapImage { UriSource = new Uri(path) };
        }
        catch (UriFormatException ex)
        {
            LogWarn("图片路径非法: " + path + " (" + ex.Message + ")");
            return null;
        }
        catch (ArgumentException ex)
        {
            LogWarn("图片路径非法: " + path + " (" + ex.Message + ")");
            return null;
        }
    }

    private static void LogWarn(string message)
    {
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Warn(message);
        }
    }
}
