namespace PdxLauncher.Core.Online;

using System.Net.Http;

/// <summary>
/// 连接超时（DNS 被污染、代理不可达、网络中断等）专用异常。
/// <para>
/// 为什么要有它：.NET 的 <see cref="HttpClient"/> 在「连不上」时抛的是
/// <see cref="OperationCanceledException"/>（内含 <see cref="TimeoutException"/>），
/// **不是** <see cref="HttpRequestException"/>。只捕 <see cref="HttpRequestException"/>
/// 的调用点会把这类失败当成「用户取消了操作」静默吞掉——页面停在「正在加载…」，
/// 列表空白，日志里只剩一条「未观察的 Task 异常」。
/// </para>
/// <para>
/// 继承 <see cref="HttpRequestException"/> 是为了兼容既有调用点：所有已经捕
/// <see cref="HttpRequestException"/> 的地方（工坊客户端、PDX Mods、账号、公告、翻译……）
/// 不改一行就能接住超时。真正的用户取消仍然原样抛 <see cref="OperationCanceledException"/>，
/// 两种语义不会混淆。
/// </para>
/// </summary>
public sealed class NetworkTimeoutException : HttpRequestException
{
    /// <param name="host">出问题的目标主机名。</param>
    /// <param name="message">诊断用的错误说明（不含界面文案）。</param>
    /// <param name="innerException">原始异常（通常是 <see cref="TimeoutException"/>）。</param>
    public NetworkTimeoutException(string host, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Host = host;
    }

    /// <summary>出问题的目标主机名，例如 <c>steamcommunity.com</c>。</summary>
    public string Host { get; }
}
