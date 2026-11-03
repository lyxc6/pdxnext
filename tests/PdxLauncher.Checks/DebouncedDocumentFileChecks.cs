namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// 缓存防抖落盘（<see cref="DebouncedDocumentFile"/>）的自检：写入先攒在内存、计时器到点才落一次盘、
/// 关机 Flush 不丢内容、删除不会把待写内容又写回来。
/// <para>
/// 这些行为错了不会崩，只会在真机上表现为「设置页刚改的缓存重启后没了」或「退出瞬间写丢最后一批」，
/// 所以口径必须钉住。
/// </para>
/// </summary>
public static class DebouncedDocumentFileChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var before = ChecksContext.Failures;

        CheckCoalesceAndTimer();
        CheckFlushAndRead();
        CheckDeleteCancelsPending();
        CheckFlushAllOnDispose();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckCoalesceAndTimer()
    {
        var inner = new FakeDocument();
        using var document = new DebouncedDocumentFile(inner, new SilentLogger(), TimeSpan.FromMilliseconds(150));

        document.WriteText("{\"a\":1}");
        document.WriteText("{\"a\":2}");
        document.WriteText("{\"a\":3}");

        ChecksContext.Check("防抖：写入后不立刻落盘", inner.Writes == 0);
        ChecksContext.Check("防抖：读到的还是最新内容", document.ReadText() == "{\"a\":3}");

        Thread.Sleep(600);

        ChecksContext.Check("防抖：计时器到点落一次盘", inner.Writes == 1);
        ChecksContext.Equal("防抖：落的是最后一次内容", "{\"a\":3}", inner.Text);
    }

    private static void CheckFlushAndRead()
    {
        var inner = new FakeDocument { Text = "{\"old\":true}" };
        using var document = new DebouncedDocumentFile(inner, new SilentLogger(), TimeSpan.FromSeconds(30));

        document.WriteText("{\"new\":true}");

        ChecksContext.Check("防抖：Flush 前没落盘", inner.Writes == 0);
        ChecksContext.Check("防抖：Flush 成功", document.Flush());
        ChecksContext.Equal("防抖：Flush 后内容已落盘", "{\"new\":true}", inner.Text);
        ChecksContext.Check("防抖：Flush 后没有待写内容", document.ReadText() == "{\"new\":true}");

        document.WriteText("{\"again\":true}");
        inner.FailWrites = true;
        ChecksContext.Check("防抖：写失败返回 false", !document.Flush());
        ChecksContext.Equal("防抖：写失败保留待写内容", "{\"again\":true}", document.ReadText());

        inner.FailWrites = false;
        ChecksContext.Check("防抖：下次 Flush 能把失败的内容补上", document.Flush());
        ChecksContext.Equal("防抖：补写后落盘", "{\"again\":true}", inner.Text);
    }

    private static void CheckDeleteCancelsPending()
    {
        var inner = new FakeDocument();
        using var document = new DebouncedDocumentFile(inner, new SilentLogger(), TimeSpan.FromMilliseconds(100));

        document.WriteText("{\"a\":1}");
        document.Delete();

        Thread.Sleep(400);

        ChecksContext.Check("防抖：删除后计时器不会把旧内容写回来", inner.Writes == 0);
        ChecksContext.Check("防抖：删除已落到内层", inner.Deleted);
    }

    private static void CheckFlushAllOnDispose()
    {
        var inner = new FakeDocument();
        var document = new DebouncedDocumentFile(inner, new SilentLogger(), TimeSpan.FromSeconds(30));
        document.WriteText("{\"bye\":true}");

        DebouncedDocumentFile.FlushAll();

        ChecksContext.Equal("防抖：FlushAll 会落掉还活着的实例", "{\"bye\":true}", inner.Text);

        document.Dispose();
        ChecksContext.Check("防抖：Dispose 后不再接受写入", !document.WriteText("{}"));
    }

    /// <summary>内存里的假文档：记录写入次数与内容，可模拟写失败。</summary>
    private sealed class FakeDocument : IDocumentFile
    {
        public string DisplayPath => "fake";

        public string? Text { get; set; }

        public int Writes { get; private set; }

        public bool Deleted { get; private set; }

        public bool FailWrites { get; set; }

        public bool Exists() => Text is not null;

        public string? ReadText() => Text;

        public bool WriteText(string json)
        {
            if (FailWrites)
            {
                return false;
            }

            Writes++;
            Text = json;
            return true;
        }

        public bool Delete()
        {
            Text = null;
            Deleted = true;
            return true;
        }
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
