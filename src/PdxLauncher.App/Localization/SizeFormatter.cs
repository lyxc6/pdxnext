namespace PdxLauncher.App.Localization;

using System.Globalization;

/// <summary>把字节数格式化成 <c>12.3 MB</c> 这种短文本（缓存占用展示用）。</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>负数按 0 处理；1024 进制，保留一位小数（B 不带小数）。</summary>
    public static string Format(long bytes)
    {
        var value = bytes < 0 ? 0d : bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var text = value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return text + " " + Units[unit];
    }
}
