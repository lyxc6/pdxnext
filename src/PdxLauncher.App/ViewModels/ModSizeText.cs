namespace PdxLauncher.App.ViewModels;

using System.Globalization;

/// <summary>
/// mod 大小文案的统一格式：不足 1KB 用 B，之后依次 KB / MB / GB，最多两位小数并去掉末尾的 0
/// （例如 1010.6KB、128.96MB、35MB）。官方启动器的大小列就是这个风格，数字与单位之间不留空格；
/// Mod 库页与播放集页共用本类，避免两处分叉。
/// </summary>
public static class ModSizeText
{
    /// <summary>1 KB 的字节数。</summary>
    private const double BytesPerKilobyte = 1024d;

    /// <summary>1 MB 的字节数。</summary>
    private const double BytesPerMegabyte = BytesPerKilobyte * 1024d;

    /// <summary>1 GB 的字节数。</summary>
    private const double BytesPerGigabyte = BytesPerMegabyte * 1024d;

    /// <summary>把字节数格式化成可读文本；未知（null 或负数）返回空串。</summary>
    /// <param name="sizeBytes">字节数，来自 mods.size。</param>
    public static string Format(long? sizeBytes)
    {
        if (sizeBytes is not { } bytes || bytes < 0)
        {
            return string.Empty;
        }

        if (bytes >= BytesPerGigabyte)
        {
            return (bytes / BytesPerGigabyte).ToString("0.##", CultureInfo.InvariantCulture) + "GB";
        }

        if (bytes >= BytesPerMegabyte)
        {
            return (bytes / BytesPerMegabyte).ToString("0.##", CultureInfo.InvariantCulture) + "MB";
        }

        if (bytes >= BytesPerKilobyte)
        {
            return (bytes / BytesPerKilobyte).ToString("0.##", CultureInfo.InvariantCulture) + "KB";
        }

        return bytes.ToString(CultureInfo.InvariantCulture) + "B";
    }
}
