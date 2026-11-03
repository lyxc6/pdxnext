namespace PdxLauncher.Checks;

using System.Text;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// <see cref="DebouncedJsonDocumentStore"/> 的自检：写入先攒着、读立刻可见、
/// 计时器到点合并落盘、删除取消待写、失败保留待写、FlushAll 兜底。
/// <para>
/// 这些语义错了的后果是「译文缓存写一次丢一次」或「关机时最后一条没落盘」，
/// 单靠跑应用很难发现，所以在假存储上把口径钉住。
/// </para>
/// </summary>
public static class DebouncedJsonDocumentStoreChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        CheckCoalesceAndFlush();
        CheckReadDeleteAndQueries();
        CheckFailureKeepsPending();
        CheckFlushAllAndDispose();

        return 0;
    }

    private static void CheckCoalesceAndFlush()
    {
        var inner = new FakeStore();
        using var store = new DebouncedJsonDocumentStore(inner, new SilentLogger());

        store.Write("cache:translations", "{\"a\":1}");
        store.Write("cache:translations", "{\"a\":2}");
        store.Write("cache:translations", "{\"a\":3}");

        ChecksContext.Check("防抖存储：计时器没到不落盘", inner.Writes == 0);
        ChecksContext.Check(
            "防抖存储：读到的永远是最后一次写入",
            store.TryRead("cache:translations", out var pending) && pending == "{\"a\":3}");

        Thread.Sleep(650);

        ChecksContext.Check("防抖存储：到点只落盘一次", inner.Writes == 1);
        ChecksContext.Check(
            "防抖存储：落盘内容为最后一次写入",
            inner.TryRead("cache:translations", out var json) && json == "{\"a\":3}");

        store.Write("cache:translations", "{\"a\":4}");
        ChecksContext.Check("防抖存储：Flush 立即落盘", store.Flush());
        ChecksContext.Check(
            "防抖存储：Flush 后内容更新",
            inner.TryRead("cache:translations", out var flushed) && flushed == "{\"a\":4}");
        ChecksContext.Check("防抖存储：Flush 后没有残留待写", store.Flush() && inner.Writes == 2);
    }

    private static void CheckReadDeleteAndQueries()
    {
        var inner = new FakeStore();
        inner.Write("cache:translations", "{\"old\":true}");
        using var store = new DebouncedJsonDocumentStore(inner, new SilentLogger());

        ChecksContext.Check(
            "防抖存储：没有待写时读到底层内容",
            store.TryRead("cache:translations", out var old) && old == "{\"old\":true}");

        store.Write("cache:translations", "{\"new\":true}");
        ChecksContext.Check("防抖存储：待写也算存在", store.Exists("cache:translations"));
        ChecksContext.Check(
            "防抖存储：List 合并待写键",
            store.List("cache:").Contains("cache:translations"));
        ChecksContext.Check(
            "防抖存储：MeasureBytes 把待写算进去",
            store.MeasureBytes("cache:") ==
                Encoding.UTF8.GetByteCount("{\"old\":true}") + Encoding.UTF8.GetByteCount("{\"new\":true}"));

        ChecksContext.Check("防抖存储：删除成功", store.Delete("cache:translations"));
        ChecksContext.Check(
            "防抖存储：删除后待写被取消（不会被计时器写回）",
            !store.TryRead("cache:translations", out _));
        ChecksContext.Check("防抖存储：删除落到下层", !inner.Exists("cache:translations"));
    }

    private static void CheckFailureKeepsPending()
    {
        var inner = new FakeStore { FailWrites = true };
        using var store = new DebouncedJsonDocumentStore(inner, new SilentLogger());

        store.Write("cache:translations", "{\"a\":1}");
        ChecksContext.Check("防抖存储：下层写失败时 Flush 返回 false", !store.Flush());
        ChecksContext.Check(
            "防抖存储：写失败后待写保留，读仍是新内容",
            store.TryRead("cache:translations", out var pending) && pending == "{\"a\":1}");

        inner.FailWrites = false;
        ChecksContext.Check("防抖存储：下层恢复后能补写", store.Flush());
        ChecksContext.Check(
            "防抖存储：补写后内容正确",
            inner.TryRead("cache:translations", out var json) && json == "{\"a\":1}");
    }

    private static void CheckFlushAllAndDispose()
    {
        var inner = new FakeStore();
        var store = new DebouncedJsonDocumentStore(inner, new SilentLogger());
        store.Write("cache:translations", "{\"a\":1}");

        DebouncedJsonDocumentStore.FlushAll();
        ChecksContext.Check(
            "防抖存储：FlushAll 落掉存活实例的待写",
            inner.TryRead("cache:translations", out var json) && json == "{\"a\":1}");

        store.Dispose();
        ChecksContext.Check("防抖存储：Dispose 后写入被拒绝", !store.Write("cache:translations", "{}"));
    }

    private sealed class FakeStore : IJsonDocumentStore
    {
        private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

        public int Writes { get; private set; }

        public bool FailWrites { get; set; }

        public bool TryRead(string key, out string? json) => _documents.TryGetValue(key, out json);

        public bool Write(string key, string json)
        {
            if (FailWrites)
            {
                return false;
            }

            Writes++;
            _documents[key] = json;
            return true;
        }

        public bool Delete(string key) => _documents.Remove(key);

        public bool Exists(string key) => _documents.ContainsKey(key);

        public long MeasureBytes(string prefix) =>
            _documents.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Sum(pair => (long)Encoding.UTF8.GetByteCount(pair.Value));

        public IReadOnlyList<string> List(string prefix) =>
            _documents.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
    }

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
