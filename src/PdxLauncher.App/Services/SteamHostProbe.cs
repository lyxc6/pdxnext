namespace PdxLauncher.App.Services;

using System;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Steam;

/// <summary>
/// <see cref="ISteamDllProbe"/> 的宿主实现：真正调 Steamworks 的活都交给
/// <see cref="SteamHostClient"/> 拉起的子进程，本进程只等结果。
/// <para>
/// <see cref="Probe"/> 是同步阻塞的（<see cref="SteamOwnershipService"/> 的既有契约），
/// 所以调用方必须在线程池上跑（DLC 页已经这么做）；内部不会捕获同步上下文，不会死锁。
/// </para>
/// </summary>
public sealed class SteamHostProbe : ISteamDllProbe
{
    private readonly SteamHostClient _client;

    /// <summary>构造。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="client">宿主客户端；默认按当前 exe 自己建一个。</param>
    public SteamHostProbe(IAppLogger logger, SteamHostClient? client = null)
    {
        _client = client ?? new SteamHostClient(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    /// <summary>宿主可执行文件是否可用。</summary>
    public bool IsAvailable => _client.IsHostAvailable;

    /// <inheritdoc />
    public SteamDllProbeResult Probe(string apiFilePath, int appId)
    {
        var result = _client.Probe(apiFilePath, appId);
        return new SteamDllProbeResult(
            result.Initialized,
            result.SteamRunning,
            result.SteamId64,
            result.Dlcs ?? Array.Empty<SteamDlcOwnership>(),
            result.Error,
            result.Detail);
    }
}
