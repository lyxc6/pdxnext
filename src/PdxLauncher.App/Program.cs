namespace PdxLauncher.App;

using System;
using System.IO;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Steam;

/// <summary>
/// 进程入口。两种身份：
/// <list type="bullet">
/// <item><c>--steam-host …</c>：Steam 宿主子进程 —— 不初始化 WinUI，跑完一次原生 Steamworks
/// 操作就退出（<see cref="SteamHostRunner"/>）。Steam 的「游戏正在运行」登记只挂在这样的
/// 短命进程上，所以主启动器不会再被 Steam 的「退出游戏」杀掉。</item>
/// <item>其它：正常启动 WinUI 界面。</item>
/// </list>
/// 本文件替代了 WinUI 生成的入口（csproj 里定义 <c>DISABLE_XAML_GENERATED_MAIN</c>）。
/// </summary>
public static class Program
{
    /// <summary>
    /// 更新流程在启动安装器前主动释放 mutex：安装器启动时若仍看到 mutex 会直接判定
    /// “程序正在运行”并退出，等不到 CloseApplications 去关窗口。
    /// </summary>
    public static void ReleaseAppMutex() => SingleInstanceGuard.ReleaseMutex();

    /// <summary>入口。</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // 性能页的启动耗时从进程入口算起；Steam 宿主子进程也走这里，但那条路径不会去看这个时间线。
        StartupTimeline.Mark(StartupTimeline.AppStart);

        if (SteamHostProtocol.IsHostInvocation(args))
        {
            try
            {
                // 宿主与客户端之间是「一行 JSON」的管道协议，两边都按 UTF-8 读写。
                // 子进程的管道默认可能跟随系统代码页（GBK），中文错误串会乱码，所以先钉死编码。
                ConfigureHostConsole();
                return SteamHostRunner.Run(args, Console.In, Console.Out, Console.Error);
            }
            catch (Exception ex)
            {
                try
                {
                    Console.Error.WriteLine("Steam 宿主异常：" + ex);
                }
                catch
                {
                    // 没有控制台可写时忽略。
                }

                return SteamHostProtocol.ExitFailure;
            }
        }

        // 单实例：第二个 UI 进程只负责把第一个实例的窗口叫到前台，然后退出。
        // 拿不到锁且确认已有实例时，优先走管道；管道连不上再弹一句「已在运行」。
        if (!SingleInstanceGuard.TryAcquire(out var alreadyRunning))
        {
            if (alreadyRunning && SingleInstanceGuard.SignalExistingInstance())
            {
                return 0;
            }

            SingleInstanceGuard.ShowAlreadyRunning();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }

    /// <summary>
    /// 宿主子进程的 stdin/stdout 固定 UTF-8（无 BOM）。没控制台可写（管道异常）时保持默认，
    /// 结果码仍然不变 —— 编码问题最多让错误文案乱码，不该让宿主直接失败。
    /// </summary>
    private static void ConfigureHostConsole()
    {
        try
        {
            var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = encoding;
            Console.OutputEncoding = encoding;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or ArgumentException)
        {
            // 保持默认编码；调用方只看结果行的前缀与 JSON。
        }
    }
}
