namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

/// <summary>
/// 图片路径（string?）→ ImageSource 的 XAML 辅助函数。
/// 返回的 BitmapImage 由框架在后台线程解码，不会阻塞 UI 线程；
/// 需要"先占位再替换"的场景请改用 <see cref="BitmapLoader"/>。
/// <para>
/// 若该路径已被 <see cref="ThemeImageFlattener"/> 摊平（EU4 的纱层背景），这里返回摊平副本；
/// 摊平是异步且只做一次的，卡片应在重建前先调 <c>AppServices.PrepareCardCoversAsync</c>。
/// </para>
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

            // 已摊平过的纱层背景（EU4 那张整幅 alpha=20% 的油画）优先用摊平副本。
            var resolved = AppServices.ThemeImages.Resolve(path) ?? path;
            if (!string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase)
                && AppServices.FileSystem.FileExists(resolved))
            {
                path = resolved;
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
