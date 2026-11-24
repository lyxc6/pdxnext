namespace PdxLauncher.App.Services;

/// <summary>
/// 全局忙态登记处：所有页面级加载与转换进度把一条租约放在这里，标题栏只显示最近开始的一项。
/// <para>
/// 用开始序号而不是栈来保证「最新操作优先」：租约可以乱序释放，旧批次结束时不会把新批次挤掉；
/// 最新一项结束后会自动回落到仍活跃的上一个。事件可能来自后台线程，订阅方自行切回 UI 线程。
/// </para>
/// </summary>
public sealed class GlobalBusyMonitor
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private long _nextSequence;

    /// <summary>当前活跃项；空闲时返回 <c>null</c>。</summary>
    public GlobalBusyItem? Current
    {
        get
        {
            lock (_gate)
            {
                return SelectCurrent();
            }
        }
    }

    /// <summary>当前显示项变化时触发；0→1、1→0 与切换到另一个标签都会触发。</summary>
    public event EventHandler? Changed;

    /// <summary>登记一条忙态；同一个标签允许多条租约，返回的对象必须 Dispose。</summary>
    /// <param name="label">标题栏显示的资源键，对应生成的 <c>Keys</c> 常量。</param>
    public Lease Begin(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        GlobalBusyItem? previous;
        Entry entry;
        lock (_gate)
        {
            previous = SelectCurrent();
            entry = new Entry(++_nextSequence, new GlobalBusyItem(_nextSequence, label));
            _entries.Add(entry);
        }

        if (!ReferenceEquals(previous, entry.Item))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return new Lease(this, entry.Sequence);
    }

    private void Release(long sequence)
    {
        GlobalBusyItem? before;
        GlobalBusyItem? after;
        lock (_gate)
        {
            before = SelectCurrent();
            var index = _entries.FindIndex(item => item.Sequence == sequence);
            if (index < 0)
            {
                return;
            }

            _entries.RemoveAt(index);
            after = SelectCurrent();
        }

        if (!ReferenceEquals(before, after))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private GlobalBusyItem? SelectCurrent()
    {
        if (_entries.Count == 0)
        {
            return null;
        }

        var newest = _entries[0];
        for (var index = 1; index < _entries.Count; index++)
        {
            if (_entries[index].Sequence > newest.Sequence)
            {
                newest = _entries[index];
            }
        }

        return newest.Item;
    }

    private sealed record Entry(long Sequence, GlobalBusyItem Item);

    /// <summary>一次忙态登记的凭据；重复 Dispose 安全。</summary>
    public sealed class Lease : IDisposable
    {
        private GlobalBusyMonitor? _owner;
        private readonly long _sequence;

        internal Lease(GlobalBusyMonitor owner, long sequence)
        {
            _owner = owner;
            _sequence = sequence;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(_sequence);
        }
    }
}

/// <summary>标题栏当前显示的一条忙态：开始序号 + 文案资源键。</summary>
/// <param name="Sequence">开始序号；大的更新。</param>
/// <param name="LabelKey">Localizer 的资源键。</param>
public sealed record GlobalBusyItem(long Sequence, string LabelKey);
