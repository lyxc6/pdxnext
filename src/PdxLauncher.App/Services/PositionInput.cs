namespace PdxLauncher.App.Services;

using System.Globalization;

/// <summary>
/// 位次输入的解析：把界面上的原文变成一个可执行的位次。
/// 纯 C#，不引用任何 XAML/UI 类型，可脱离 UI 自检。
/// </summary>
public static class PositionInput
{
    /// <summary>
    /// 严格解析用户输入的位次：只接受纯 ASCII 十进制数字，语义为 1 基。
    /// 小于 1 视为无效；大于 <paramref name="count"/> 视为**置底**（钳到 count），
    /// 因为「输入一个比总数大的数放最后」是自然直觉，报错只会让人以为功能坏了。
    /// 显式拒绝全角数字、中文数字、正负号、小数点、空串 —— 这些在中文输入法下都可能被敲出来，
    /// 而本仓库部署的 WinUI 2.3.9 没有 <c>KeyRoutedEventArgs.IsCommitted</c>，
    /// 无法在按键层面区分「输入法确认」与「提交」，所以判定放在文本层，结果才是确定的。
    /// </summary>
    /// <param name="text">输入框原文。</param>
    /// <param name="count">当前可见行数，也就是最大位次。</param>
    /// <param name="position">解析出的位次（已钳到 1..<paramref name="count"/>）。</param>
    /// <returns>false 表示输入本身非法（空、非纯数字、小于 1，或压根没有行）。</returns>
    public static bool TryParse(string? text, int count, out int position)
    {
        position = 0;
        if (count < 1)
        {
            // 没有行可编辑，任何输入都没有意义。
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            // 一长串数字（例如粘进去 99999999999999）会溢出，按非法处理。
            return false;
        }

        if (value < 1)
        {
            // 0 与负数仍然无效：0 既可读作「置顶」也可读作「误触」，不猜。
            return false;
        }

        position = value > count ? count : value;
        return true;
    }
}
