namespace PdxLauncher.App.Services;

/// <summary>
/// 工坊详情截图区的「还剩几张没落地」计数。
/// <para>
/// 顶部「正在下载截图…」的显示与否只看这里，不再看「整批下载任务」是否结束 ——
/// 否则正文配图（另一批网络请求）会把提示一直挂着。计数口径：<see cref="Begin"/> 记下本次条目数，
/// 每张图在自己的 finally 里 <see cref="Settle"/> 一次，<see cref="IsBusy"/> 是「还有没结算的」。
/// </para>
/// <para>
/// 约定：只在 UI 线程调用（视图模型的 <c>await</c> 延续都在 UI 线程），内部不加锁。
/// </para>
/// </summary>
public sealed class ScreenshotLoadTracker
{
    private int _total;
    private int _settled;

    /// <summary>最近一次 <see cref="Begin"/> 记下的条目数。</summary>
    public int Total => _total;

    /// <summary>已经结算（成功 / 失败 / 取消）的条目数。</summary>
    public int Settled => _settled;

    /// <summary>还没结算的条目数；永不为负。</summary>
    public int Pending => Math.Max(0, _total - _settled);

    /// <summary>是否还有截图没结算 —— 界面上的「正在下载截图…」就看它。</summary>
    public bool IsBusy => Pending > 0;

    /// <summary>开始一批下载。</summary>
    /// <param name="total">本批条目数；负数按 0 记。</param>
    public void Begin(int total)
    {
        _total = Math.Max(0, total);
        _settled = 0;
    }

    /// <summary>结算一张。</summary>
    /// <returns>这次调用是否真的推进了计数（重复结算、超出条目数时为 <c>false</c>）。</returns>
    public bool Settle()
    {
        if (_settled >= _total)
        {
            return false;
        }

        _settled++;
        return true;
    }

    /// <summary>把剩余条目一次性结算（取消 / 收尾用，保证提示不会卡住）。</summary>
    public void FinishAll() => _settled = _total;

    /// <summary>清空计数（换 mod、重新加载时用）。</summary>
    public void Reset()
    {
        _total = 0;
        _settled = 0;
    }
}
