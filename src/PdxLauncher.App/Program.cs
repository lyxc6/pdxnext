namespace PdxLauncher.App;

using System;
using System.IO;
using System.Threading;

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
    /// 安装器用这个 mutex 判断启动器是否仍在运行（`AppMutex`），升级/卸载前可以提示关闭。
    /// 只由正常 UI 进程创建；Steam 宿主子进程是短命的，不能占用它。
    /// </summary>
    private static Mutex? _appMutex;

    /// <summary>
    /// 更新流程在启动安装器前主动释放 mutex：安装器启动时若仍看到 mutex 会直接判定
    /// “程序正在运行”并退出，等不到 CloseApplications 去关窗口。
    /// </summary>
    public static void ReleaseAppMutex()
    {
        var mutex = Interlocked.Exchange(ref _appMutex, null);
        mutex?.Dispose();
    }

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
                return SteamHostRunner.Run(args, Console.Out, Console.Error);
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

        // 保持到进程结束；安装器/uninstaller 通过同名 mutex 判断主程序是否还在运行。
        _appMutex = new Mutex(initiallyOwned: false, name: @"Local\PdxNext.App");

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
