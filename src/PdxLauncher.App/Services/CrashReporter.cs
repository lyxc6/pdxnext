namespace PdxLauncher.App.Services;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 崩溃可观测性：把未处理异常落成崩溃记录文件，并尽量给用户一个「可复制」的错误框。
/// <para>
/// 这里的两条硬约束：
/// 1. <b>绝不抛异常</b>。崩溃处理路径上再抛一次，用户就只剩 WER 静默退出了。
/// 2. <b>不阻塞 UI 线程</b>。异常可能就发生在 UI 线程上，写文件走线程池，弹框走 DispatcherQueue。
/// </para>
/// </summary>
public static class CrashReporter
{
    /// <summary>崩溃记录目录（与日志同根，便于一起打包）。</summary>
    public static string CrashDirectory => Path.Combine(AppServices.DataRoot, "logs");

    /// <summary>
    /// 记录一次未处理异常。返回崩溃记录路径；写盘失败时返回 null（此时只写日志）。
    /// </summary>
    /// <param name="message">一句话说明。</param>
    /// <param name="exception">异常本体。</param>
    /// <param name="context">可选上下文（页面名 / gameId 等）。</param>
    public static string? Record(string message, Exception? exception, string? context = null)
    {
        string? path = null;
        try
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error(message, exception);
            }

            var writer = new CrashReportWriter(
                AppServices.FileSystem,
                AppServices.Clock,
                CrashDirectory);
            var result = writer.Write(message, exception, context);
            if (result.IsSuccess)
            {
                path = result.Value;
            }
            else if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("写崩溃记录失败: " + result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // 崩溃记录本身失败时不能再抛，能写日志就写。
            try
            {
                AppServices.Logger.Error("写崩溃记录时二次异常", ex);
            }
            catch
            {
                // 日志也失败：放弃，绝不让这里冒泡。
            }
        }

        return path;
    }

    /// <summary>
    /// 在 UI 线程上弹一个可复制的错误框。调用方可以是任意线程；
    /// 没有窗口 / 没有 XamlRoot / 已有别的对话框时静默放弃。
    /// </summary>
    public static void ShowDialog(string message, Exception? exception, string? crashPath)
    {
        try
        {
            var window = App.MainWindowInstance;
            if (window?.Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return;
            }

            var dispatcher = root.DispatcherQueue;
            if (dispatcher is null)
            {
                return;
            }

            if (!dispatcher.TryEnqueue(DispatcherQueuePriority.High, () => ShowDialogOnUiThread(root.XamlRoot, message, exception, crashPath)))
            {
                // 队列已满 / 已关闭：放弃弹框，崩溃记录还在。
            }
        }
        catch
        {
            // 弹框失败不影响已经写好的崩溃记录。
        }
    }

    private static void ShowDialogOnUiThread(
        XamlRoot xamlRoot,
        string message,
        Exception? exception,
        string? crashPath)
    {
        try
        {
            var localizer = Localizer.Instance;
            var body = new System.Text.StringBuilder(512);
            body.Append(localizer.T(Keys.ShellCrashMessage, exception?.Message ?? message));

            if (!string.IsNullOrEmpty(crashPath))
            {
                body.Append("\n\n").Append(localizer.T(Keys.ShellCrashPath, crashPath));
            }

            body.Append("\n\n").Append(localizer.ShellCrashHint);

            var details = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 240,
                Text = BuildDetails(message, exception, crashPath),
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = body.ToString(),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
            panel.Children.Add(details);

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = localizer.ShellCrashTitle,
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                CloseButtonText = localizer.CommonClose,
                PrimaryButtonText = localizer.ShellCrashCopy,
                DefaultButton = ContentDialogButton.Close,
            };

            dialog.PrimaryButtonClick += async (_, _) =>
            {
                try
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(details.Text);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                }
                catch
                {
                    // 剪贴板被占用等：忽略，用户仍可手动选中复制。
                }
            };

            _ = dialog.ShowAsync();
        }
        catch
        {
            // 弹框失败不影响已经写好的崩溃记录。
        }
    }

    private static string BuildDetails(string message, Exception? exception, string? crashPath)
    {
        var builder = new System.Text.StringBuilder(1024);
        if (!string.IsNullOrEmpty(crashPath))
        {
            builder.Append("crash: ").Append(crashPath).Append('\n');
        }

        builder.Append(CrashReportWriter.Compose(message, exception, null));
        return builder.ToString();
    }
}
