namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// <see cref="SteamHostFailureClassifier"/> 的判定口径自检。
/// <para>
/// 输入取自 2026-11-11 真机日志：结构化 <c>Cannot create IPC pipe</c>，
/// 以及原生 tier0 fatal assert 后宿主没有结果行、只留下 stderr 的文本。
/// 同时钉住「不要过度匹配」：单独的 Fatal assert / 通用 Init 失败 / 超时都不能算 Steam 没运行。
/// </para>
/// </summary>
public static class SteamHostFailureClassifierChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckNativeFailuresAreClassified();
        CheckNoOverMatching();
        CheckRetryGate();
        return ChecksContext.Failures - before;
    }

    private static void CheckNativeFailuresAreClassified()
    {
        const string IpcError =
            "SteamAPI_Init 失败：Cannot create IPC pipe to Steam client process.  Steam is probably not running.";
        ChecksContext.Check(
            "IPC 管道失败归为 Steam 不可用",
            SteamHostFailureClassifier.IsSteamClientUnavailable(IpcError));

        const string Tier0Assert =
            @"src\tier0\threadtools.cpp (2157) : Assertion Failed: Bad Event handle (handle 0x0000000000000000)  src\common\processpipe_any.cpp (457) : Assertion Failed: CCrossProcessPipe::BWrite GetLastError=6  src\common\pipes.cpp (879) : CClientPipe::BWriteAndReadResult: BWrite failed  src\common\pipes.cpp (879) : Fatal assert; application exiting";
        ChecksContext.Check(
            "tier0 断言归为 Steam 不可用",
            SteamHostFailureClassifier.IsSteamClientUnavailable(Tier0Assert));

        ChecksContext.Check(
            "宿主预检文案归为 Steam 不可用",
            SteamHostFailureClassifier.IsSteamClientUnavailable("Steam 客户端没有运行"));

        ChecksContext.Check(
            "Init 兜底文案归为 Steam 不可用",
            SteamHostFailureClassifier.IsSteamClientUnavailable("SteamAPI_Init 失败（Steam 未运行或未登录）"));

        ChecksContext.Check(
            "IsSteamRunning 失败归为 Steam 不可用",
            SteamHostFailureClassifier.IsSteamClientUnavailable("SteamAPI_IsSteamRunning() failed."));
    }

    private static void CheckNoOverMatching()
    {
        ChecksContext.Check(
            "单独的 Fatal assert 不归为 Steam 不可用",
            !SteamHostFailureClassifier.IsSteamClientUnavailable("Fatal assert; application exiting"));

        ChecksContext.Check(
            "通用 Init 失败不归为 Steam 不可用",
            !SteamHostFailureClassifier.IsSteamClientUnavailable("SteamAPI_Init 失败：Invalid AppID"));

        ChecksContext.Check(
            "宿主超时不归为 Steam 不可用",
            !SteamHostFailureClassifier.IsSteamClientUnavailable("宿主进程超时未返回结果"));

        ChecksContext.Check(
            "空错误不归为 Steam 不可用",
            !SteamHostFailureClassifier.IsSteamClientUnavailable(null));
        ChecksContext.Check(
            "空白错误不归为 Steam 不可用",
            !SteamHostFailureClassifier.IsSteamClientUnavailable("  "));
    }

    private static void CheckRetryGate()
    {
        const string IpcError = "Cannot create IPC pipe to Steam client process.";
        ChecksContext.Check(
            "IPC 失败 + 客户端仍在 → 重试一次",
            SteamHostFailureClassifier.ShouldRetry(IpcError, true));
        ChecksContext.Check(
            "IPC 失败 + 客户端明确不在 → 不重试",
            !SteamHostFailureClassifier.ShouldRetry(IpcError, false));
        ChecksContext.Check(
            "IPC 失败 + 进程检测未知 → 不重试",
            !SteamHostFailureClassifier.ShouldRetry(IpcError, null));
        ChecksContext.Check(
            "非 IPC 失败 + 客户端仍在 → 不重试",
            !SteamHostFailureClassifier.ShouldRetry("SteamAPI_Init 失败：Invalid AppID", true));
    }
}
