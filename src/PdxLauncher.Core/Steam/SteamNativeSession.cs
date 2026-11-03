namespace PdxLauncher.Core.Steam;

/// <summary>
/// 同一进程里「谁在用 Steam 原生接口」的闸门。
/// <para>
/// steam_api 是单例：同一进程里两份 DLL 同时 <c>SteamAPI_Init</c> 会互相踩。
/// 工坊订阅与 DLC 授权探测共用这一把闸门，拿不到就降级为「稍后重试」，谁都不阻塞 UI。
/// 现在两者都只在 <c>--steam-host</c> 子进程里跑（宿主一次只做一件事），闸门因此几乎不会冲突。
/// </para>
/// </summary>
public static class SteamNativeSession
{
    private static readonly object Gate = new();

    private static string? _owner;

    /// <summary>当前是否有人占着 Steam 原生接口。</summary>
    public static bool IsActive
    {
        get
        {
            lock (Gate)
            {
                return _owner is not null;
            }
        }
    }

    /// <summary>当前占用者（便于日志排查）；没人占用时为 null。</summary>
    public static string? Owner
    {
        get
        {
            lock (Gate)
            {
                return _owner;
            }
        }
    }

    /// <summary>
    /// 尝试占用闸门。成功返回 true，并给出必须 Dispose 的作用域；
    /// 已被别人占用时返回 false（<paramref name="scope"/> 是一个空作用域，Dispose 无副作用）。
    /// </summary>
    public static bool TryEnter(string ownerName, out IDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(ownerName);
        lock (Gate)
        {
            if (_owner is not null)
            {
                scope = EmptyScope.Instance;
                return false;
            }

            _owner = ownerName;
            scope = new Scope(ownerName);
            return true;
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _ownerName;
        private bool _disposed;

        public Scope(string ownerName) => _ownerName = ownerName;

        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (string.Equals(_owner, _ownerName, StringComparison.Ordinal))
                {
                    _owner = null;
                }
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose()
        {
        }
    }
}
