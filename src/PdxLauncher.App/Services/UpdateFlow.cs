namespace PdxLauncher.App.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.Core.Updates;

/// <summary>
/// 把 <see cref="UpdateService"/> 接到全局浮层上：启动静默检查、发现更新后的确认、
/// 下载进度与安装器启动。所有 UI 调用都要求已经处于窗口线程。
/// </summary>
internal static class UpdateFlow
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>启动时调用。自动检查失败只写日志，不打扰用户。</summary>
    public static async Task CheckOnStartupAsync()
    {
        try
        {
            var result = await CheckAsync(manual: false).ConfigureAwait(true);
            if (result is { Status: UpdateCheckStatus.Failed })
            {
                AppServices.Logger.Warn("自动检查更新失败: " + result.Message);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("自动检查更新异常: " + ex.Message);
        }
    }

    /// <summary>
    /// 检查更新；发现可用版本时弹确认框，用户确认后走下载/校验/安装。
    /// 返回 null 表示已有检查在跑或更新服务不可用。
    /// </summary>
    public static async Task<UpdateCheckResult?> CheckAsync(bool manual)
    {
        if (AppServices.Updates is null)
        {
            return null;
        }

        if (!await Gate.WaitAsync(0).ConfigureAwait(true))
        {
            return null;
        }

        try
        {
            var result = await AppServices.Updates.CheckAsync(manual, CancellationToken.None).ConfigureAwait(true);
            if (result.HasUpdate && result.Manifest is { } manifest)
            {
                await ShowAvailableAsync(manifest).ConfigureAwait(true);
            }
            else if (manual && result.Status == UpdateCheckStatus.Failed)
            {
                await ShowMessageAsync(
                    Localizer.Instance.T(Keys.SettingsAboutTitle),
                    Localizer.Instance.T(Keys.SettingsAboutCheckFailed, result.Message)).ConfigureAwait(true);
            }

            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task ShowAvailableAsync(UpdateManifest manifest)
    {
        var localizer = Localizer.Instance;
        var panel = new StackPanel { Spacing = 8, MaxWidth = 520 };

        panel.Children.Add(new TextBlock
        {
            Text = localizer.T(
                Keys.UpdateAvailableBody,
                AppServices.Updates.CurrentVersion,
                manifest.Version),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
        {
            var notes = manifest.ReleaseNotes.Trim();
            if (notes.Length > 1200)
            {
                notes = notes[..1200] + "…";
            }

            panel.Children.Add(new TextBlock
            {
                Text = notes,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.78,
            });
        }

        if (manifest.Mandatory)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.T(Keys.UpdateMandatoryHint),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
        }

        if (AppServices.Updates.IsPerMachineInstall)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.T(Keys.UpdatePerMachineHint),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.78,
            });
        }

        var install = await ConfirmOverlay.ConfirmAsync(
            localizer.T(Keys.UpdateTitleAvailable, manifest.Version),
            panel,
            localizer.T(Keys.UpdateInstall),
            manifest.Mandatory ? null : localizer.T(Keys.UpdateLater)).ConfigureAwait(true);

        if (install)
        {
            await DownloadAndInstallAsync(manifest).ConfigureAwait(true);
        }
    }

    private static async Task DownloadAndInstallAsync(UpdateManifest manifest)
    {
        var host = ConfirmOverlay.Host;
        if (host is null)
        {
            return;
        }

        var localizer = Localizer.Instance;
        var status = new TextBlock
        {
            Text = localizer.T(Keys.UpdateDownloading),
            TextWrapping = TextWrapping.Wrap,
        };
        var progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Minimum = 0,
            Maximum = 100,
            Width = 420,
        };
        var panel = new StackPanel { Spacing = 10, MaxWidth = 520 };
        panel.Children.Add(status);
        panel.Children.Add(progressBar);

        using var cancellation = new CancellationTokenSource();
        var dialog = host.ShowAsync(
            localizer.T(Keys.UpdateDownloading),
            panel,
            localizer.T(Keys.UpdateDownloadCancel),
            cancelText: null,
            onConfirm: cancellation.Cancel);

        try
        {
            var progress = new Progress<UpdateProgress>(value =>
            {
                if (value.Fraction is { } fraction)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = Math.Clamp(fraction * 100, 0, 100);
                    status.Text = FormatProgress(value);
                }
                else
                {
                    status.Text = FormatProgress(value);
                }
            });

            var setupPath = await AppServices.Updates
                .DownloadAsync(manifest, progress, cancellation.Token)
                .ConfigureAwait(true);

            host.Close();

            // 安装器会检查 AppMutex；先释放再启动，让 CloseApplications 有机会正常关窗。
            Program.ReleaseAppMutex();
            var launch = AppServices.Updates.LaunchInstaller(setupPath);
            if (launch.ElevationCanceled)
            {
                await ShowMessageAsync(
                    localizer.T(Keys.UpdateTitleAvailable, manifest.Version),
                    localizer.T(Keys.UpdateElevationCanceled)).ConfigureAwait(true);
                return;
            }

            if (!launch.Started)
            {
                await ShowMessageAsync(
                    localizer.T(Keys.UpdateTitleAvailable, manifest.Version),
                    localizer.T(Keys.UpdateInstallFailed, launch.Error ?? string.Empty)).ConfigureAwait(true);
                return;
            }

            AppServices.SaveSettings();
            AppServices.FlushSettings();
            AppServices.Logger.Info("更新安装器已启动，关闭启动器以便替换文件");

            // 关闭窗口会触发 OnWindowClosed 的清理与设置落盘；安装器由 Inno 负责收尾。
            App.MainWindowInstance?.Close();
        }
        catch (OperationCanceledException)
        {
            host.Close();
        }
        catch (Exception ex)
        {
            host.Close();
            AppServices.Logger.Warn("下载或启动更新失败: " + ex.Message);
            await ShowMessageAsync(
                localizer.T(Keys.UpdateTitleAvailable, manifest.Version),
                localizer.T(Keys.UpdateDownloadFailed, ex.Message)).ConfigureAwait(true);
        }
        finally
        {
            await dialog.ConfigureAwait(true);
        }
    }

    private static string FormatProgress(UpdateProgress progress)
    {
        var received = FormatBytes(progress.BytesReceived);
        return progress.TotalBytes is > 0
            ? received + " / " + FormatBytes(progress.TotalBytes.Value)
            : received;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024L * 1024L => (bytes / 1024d / 1024d / 1024d).ToString("0.0") + " GB",
        >= 1024L * 1024L => (bytes / 1024d / 1024d).ToString("0.0") + " MB",
        >= 1024L => (bytes / 1024d).ToString("0.0") + " KB",
        _ => bytes + " B",
    };

    private static Task ShowMessageAsync(string title, string message) =>
        ConfirmOverlay.ShowMessage(title, message, Localizer.Instance.T(Keys.UpdateLater));
}
