namespace PdxLauncher.App.Services;

/// <summary>
/// 单个仪表的最近 N 秒采样环形缓冲：每秒追加一个百分比，按时间淘汰过期点。
/// 只依赖 System.*，不碰 WinUI，由 GaugeTrendChecks 钉住容量与淘汰口径。
/// </summary>
public sealed class GaugeTrendBuffer
{
    private readonly Queue<(DateTimeOffset At, double Percent)> _samples = new();

    /// <summary>追加一个采样点（同一秒内重复追加会覆盖最后一个，避免瞬时抖动塞满窗口）。</summary>
    public void Append(double percent, DateTimeOffset now)
    {
        var clamped = PerformanceGaugeMath.ClampPercent(percent);
        if (_samples.Count > 0)
        {
            var last = _samples.Last();
            if ((now - last.At).TotalMilliseconds < GaugeTrendMath.DedupeMilliseconds)
            {
                _samples.Dequeue();
                _samples.Enqueue((now, clamped));
                return;
            }
        }

        _samples.Enqueue((now, clamped));
        Trim(now);
    }

    /// <summary>取当前窗口内的百分比快照（从旧到新）。</summary>
    public IReadOnlyList<double> Values()
    {
        Trim(DateTimeOffset.Now);
        return _samples.Select(sample => sample.Percent).ToArray();
    }

    /// <summary>清空历史（语言切换或重置时用）。</summary>
    public void Clear() => _samples.Clear();

    /// <summary>淘汰超过窗口的旧点；传入 now 便于自检不依赖真实时钟。</summary>
    private void Trim(DateTimeOffset now)
    {
        while (_samples.Count > 0 && (now - _samples.Peek().At).TotalSeconds > GaugeTrendMath.WindowSeconds)
        {
            _samples.Dequeue();
        }
    }
}
