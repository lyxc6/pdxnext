namespace PdxLauncher.App.Services;

/// <summary>
/// 翻译在途计数器：界面据此显示「正在翻译…」的环形进度与文字。
/// <para>
/// 只关心「此刻有没有翻译请求在途」，不关心是谁发起的——列表页的批量标题翻译、
/// 详情页的逐段正文翻译都会经过 <see cref="TranslationCoordinator.TranslateBatchAsync"/>，
/// 在那一处登记，所以切页也不会漏掉在途状态。
/// </para>
/// <para>
/// 事件可能从后台线程触发：订阅方自己负责切换线程（本仓库约定）。
/// </para>
/// </summary>
public sealed class TranslationActivityMonitor
{
    private readonly object _gate = new();

    private int _count;

    /// <summary>在途状态翻转时触发：只在 0→1（开始翻译）与 1→0（全部结束）各触发一次。</summary>
    public event EventHandler? Changed;

    /// <summary>当前是否有翻译请求在途。</summary>
    public bool IsActive => Volatile.Read(ref _count) > 0;

    /// <summary>
    /// 登记一次在途翻译；返回的租约 Dispose 时注销。可以嵌套调用，计数归零才算结束。
    /// </summary>
    /// <returns>租约；重复 Dispose 是安全的。</returns>
    public Lease Begin()
    {
        bool raise;
        lock (_gate)
        {
            raise = _count++ == 0;
        }

        if (raise)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return new Lease(this);
    }

    private void Release()
    {
        bool raise;
        lock (_gate)
        {
            if (_count == 0)
            {
                // 兜底：租约被多释放一次也不该让计数变成负数，否则界面会永远显示「正在翻译」。
                return;
            }

            raise = --_count == 0;
        }

        if (raise)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>一次在途登记的凭据：Dispose 时把计数减一。</summary>
    public sealed class Lease : IDisposable
    {
        private TranslationActivityMonitor? _owner;

        internal Lease(TranslationActivityMonitor owner) => _owner = owner;

        /// <inheritdoc />
        public void Dispose()
        {
            // Interlocked 换空保证多线程/重复 Dispose 只减一次。
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
