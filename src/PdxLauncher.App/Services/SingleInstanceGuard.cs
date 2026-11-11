namespace PdxLauncher.App.Services;

using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using PdxLauncher.App.Localization;

/// <summary>
/// 单实例保护：第二个 UI 进程不再创建窗口，而是通过命名管道让第一个实例把窗口前置。
/// <para>
/// 以前只在 <see cref="Program"/> 里创建了同名 Mutex 给安装器探测「程序是否在运行」，
/// 但从不检查 <c>createdNew</c>，所以快速重启 / 双击两次可以并存两个进程，共享同一个
/// <c>data</c> 目录并并发写 settings / SQLite。这里把 Mutex 升级成真正的单实例锁，
/// 管道只传一个 <c>show</c>，不传任何业务数据。
/// </para>
/// </summary>
public static class SingleInstanceGuard
{
    /// <summary>安装器 <c>AppMutex</c> 读的就是这个名字，不能改。</summary>
    public const string MutexName = @"Local\PdxNext.App";

    /// <summary>同会话内的激活管道名（NamedPipe 自带会话内可见性，不需要 Local 前缀）。</summary>
    public const string ActivationPipeName = "PdxNext.App.Activate";

    private const int SwRestore = 9;
    private const uint MbOk = 0x00000000;
    private const uint MbIconInformation = 0x00000040;

    private static Mutex? _mutex;
    private static CancellationTokenSource? _serverCts;

    /// <summary>第一个实例拿锁；已有实例时返回 false，调用方应尝试唤醒它并退出。</summary>
    /// <param name="alreadyRunning">true = 锁被另一个进程持有。</param>
    public static bool TryAcquire(out bool alreadyRunning)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        alreadyRunning = !createdNew;
        if (alreadyRunning)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    /// <summary>通知第一个实例把主窗口前置；连接不上返回 false。</summary>
    public static bool SignalExistingInstance()
    {
        // 第一个实例可能还在 ComposeServices / 创建窗口，管道服务器要等窗口激活后才起。
        // 给 2 秒窗口（4 次 500 ms），连不上再退化成 MessageBox。
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    ActivationPipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                client.Connect(500);
                using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine("show");
                writer.Flush();
                return true;
            }
            catch (TimeoutException)
            {
                // 第一个实例还没开始监听，重试。
            }
            catch (IOException)
            {
                // 管道刚断开或服务器正在重建，重试。
            }
            catch (UnauthorizedAccessException)
            {
                // 另一个用户/完整性级别占着同名的管道；退化成 MessageBox。
                break;
            }
        }

        return false;
    }

    /// <summary>在 UI 线程上启动管道监听；只在第一个实例里调用一次。</summary>
    /// <param name="dispatcher">主窗口的 DispatcherQueue。</param>
    /// <param name="window">要前置的主窗口。</param>
    public static void StartActivationServer(DispatcherQueue dispatcher, Window window)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(window);

        if (_serverCts is not null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _serverCts = cts;
        _ = Task.Run(() => RunActivationServerAsync(dispatcher, window, cts.Token));
    }

    /// <summary>停止监听（应用退出时可选调用；进程退出本身也会收掉管道）。</summary>
    public static void StopActivationServer()
    {
        var cts = Interlocked.Exchange(ref _serverCts, null);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经自己收尾了。
        }

        cts.Dispose();
    }

    /// <summary>更新安装器启动前释放 Mutex（原 <c>Program.ReleaseAppMutex</c> 的语义）。</summary>
    public static void ReleaseMutex()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        mutex?.Dispose();
    }

    /// <summary>
    /// 安装器取消 / 启动失败后重新拿回单实例锁。
    /// <para>
    /// 更新流程会先释放 Mutex 让安装器的 CloseApplications 能关窗口；如果 UAC 被取消或
    /// 安装器没起来，必须把锁拿回来，否则当前进程继续跑却没有锁，下一次双击会开出第二个实例。
    /// </para>
    /// </summary>
    /// <returns>true = 重新拿到；false = 锁已被另一个实例占用（或无法创建）。</returns>
    public static bool TryReacquire()
    {
        if (_mutex is not null)
        {
            return true;
        }

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return false;
            }

            _mutex = mutex;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            mutex?.Dispose();
            return false;
        }
    }

    /// <summary>通道连不上时的兜底提示；不崩、不递归。</summary>
    public static void ShowAlreadyRunning()
    {
        string text;
        try
        {
            text = Localizer.Instance.T(Keys.SingleInstanceAlreadyRunning);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 资源还没初始化（极早期失败）时也不能因为一句提示崩掉第二个进程。
            text = "PdxNext is already running.";
        }

        _ = MessageBoxW(IntPtr.Zero, text, "PdxNext", MbOk | MbIconInformation);
    }

    private static async Task RunActivationServerAsync(
        DispatcherQueue dispatcher,
        Window window,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    ActivationPipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(command, "show", StringComparison.Ordinal))
                {
                    dispatcher.TryEnqueue(() => ActivateWindow(window));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // 客户端连上又断开：重建服务器，别让监听器直接死掉。
                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                AppServices.Logger.Warn("单实例激活监听失败: " + ex.Message);
                try
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static void ActivateWindow(Window window)
    {
        try
        {
            window.AppWindow.Show();
            window.Activate();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _ = ShowWindow(hwnd, SwRestore);
            _ = SetForegroundWindow(hwnd);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppServices.Logger.Warn("前置已有窗口失败: " + ex.Message);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
