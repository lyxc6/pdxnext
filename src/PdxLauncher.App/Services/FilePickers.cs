namespace PdxLauncher.App.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Windows.Storage.Pickers;

using WinRT.Interop;

/// <summary>
/// 系统文件选择器的统一入口。
/// <para>
/// WinUI 3 的非打包应用必须在弹选择器前用 <c>InitializeWithWindow</c> 绑定主窗口 HWND，
/// 否则选择器会直接抛异常。项目里以前只有 ModStudio 的图片选择器手写了一遍，导入/导出播放集
/// 不要再复制第二份，统一走这里。
/// </para>
/// </summary>
internal static class FilePickers
{
    /// <summary>播放集分享文件的扩展名（带点）。</summary>
    public const string JsonExtension = ".json";

    /// <summary>选一个 <c>.json</c> 文件；用户取消或窗口未就绪时返回 null。</summary>
    public static async Task<string?> PickOpenJsonAsync()
    {
        if (App.MainWindowInstance is null)
        {
            return null;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(JsonExtension);

        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(App.MainWindowInstance));

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// 选一个保存位置，默认文件名为「播放集名.json」；用户取消时返回 null。
    /// </summary>
    public static async Task<string?> PickSaveJsonAsync(string suggestedFileName)
    {
        if (App.MainWindowInstance is null)
        {
            return null;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = SanitizeFileName(suggestedFileName),
        };
        picker.FileTypeChoices.Add("JSON", new List<string> { JsonExtension });

        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(App.MainWindowInstance));

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// 播放集名可能带 <c>/</c>、<c>:</c> 等文件系统非法字符，直接塞给保存对话框会被拒绝；
    /// 统一替换成下划线，并裁到 120 字符（Windows 的路径长度与对话框显示都更稳）。
    /// </summary>
    internal static string SanitizeFileName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            trimmed = "playset";
        }

        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[trimmed.Length];
        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            buffer[index] = Array.IndexOf(invalid, character) >= 0 ? '_' : character;
        }

        var sanitized = new string(buffer).Trim();
        if (sanitized.Length == 0)
        {
            sanitized = "playset";
        }

        // Windows 不允许文件名以点或空格结尾。
        sanitized = sanitized.TrimEnd('.', ' ');
        return sanitized.Length > 120 ? sanitized[..120].TrimEnd() : sanitized;
    }
}
