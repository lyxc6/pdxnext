namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 图片宽度缓存自检。x:Bind 每次求值都要问「这张图多宽」，没缓存时每问一次就开文件读 16 KB 头，
/// 全部落在 UI 线程上。缓存口径错了不会崩，只会悄悄退回「每次都读盘」或者显示错误尺寸，
/// 所以命中 / 失败 / 清空这几种情形必须钉住。
/// </summary>
public static class ImageProbeCacheChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        ImageProbeCache.Clear();
        try
        {
            CheckProbesOnce();
            CheckCachesFailure();
            CheckCaseInsensitive();
            CheckSeparatePaths();
            CheckClear();
            CheckRejectsEmptyPath();
        }
        finally
        {
            ImageProbeCache.Clear();
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckProbesOnce()
    {
        ImageProbeCache.Clear();
        var calls = 0;
        int Probe(string path)
        {
            calls++;
            return 1920;
        }

        var first = ImageProbeCache.GetWidth(@"C:\covers\a.png", Probe);
        var second = ImageProbeCache.GetWidth(@"C:\covers\a.png", Probe);

        ChecksContext.Equal("图片宽度：首次探测返回实测值", 1920, first);
        ChecksContext.Equal("图片宽度：第二次命中缓存", 1920, second);
        ChecksContext.Equal("图片宽度：同一路径只探测一次", 1, calls);
    }

    private static void CheckCachesFailure()
    {
        ImageProbeCache.Clear();
        var calls = 0;
        int Probe(string path)
        {
            calls++;
            return 0;
        }

        var first = ImageProbeCache.GetWidth(@"C:\covers\broken.png", Probe);
        var second = ImageProbeCache.GetWidth(@"C:\covers\broken.png", Probe);

        ChecksContext.Equal("图片宽度：读不出宽度记 0", 0, first);
        ChecksContext.Equal("图片宽度：失败也缓存，不反复读坏文件", 0, second);
        ChecksContext.Equal("图片宽度：坏文件只探测一次", 1, calls);
    }

    private static void CheckCaseInsensitive()
    {
        ImageProbeCache.Clear();
        var calls = 0;
        int Probe(string path)
        {
            calls++;
            return 800;
        }

        ImageProbeCache.GetWidth(@"C:\Covers\B.png", Probe);
        var width = ImageProbeCache.GetWidth(@"c:\covers\b.png", Probe);

        ChecksContext.Equal("图片宽度：路径大小写不同视为同一张", 800, width);
        ChecksContext.Equal("图片宽度：大小写变体共用缓存", 1, calls);
    }

    private static void CheckSeparatePaths()
    {
        ImageProbeCache.Clear();
        var calls = 0;
        int Probe(string path)
        {
            calls++;
            return path.Length;
        }

        ImageProbeCache.GetWidth(@"C:\covers\a.png", Probe);
        ImageProbeCache.GetWidth(@"C:\covers\c.png", Probe);

        ChecksContext.Equal("图片宽度：不同路径各自探测", 2, calls);
    }

    private static void CheckClear()
    {
        ImageProbeCache.Clear();
        var calls = 0;
        int Probe(string path)
        {
            calls++;
            return 100;
        }

        ImageProbeCache.GetWidth(@"C:\covers\a.png", Probe);
        ImageProbeCache.Clear();
        ImageProbeCache.GetWidth(@"C:\covers\a.png", Probe);

        ChecksContext.Equal("图片宽度：清空后重新探测", 2, calls);
    }

    private static void CheckRejectsEmptyPath()
    {
        var threw = false;
        try
        {
            ImageProbeCache.GetWidth("   ", static _ => 1);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        ChecksContext.Check("图片宽度：空白路径直接拒绝", threw);
    }
}
