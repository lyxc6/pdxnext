namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 仪表迷你趋势线数学的自检：空数据、单点、全平与上下界的映射边界。
/// 映射错了不会崩，只会让趋势线画反或贴边被裁，编译器看不见，所以由这里钉住。
/// </summary>
public static class GaugeTrendChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var before = ChecksContext.Failures;

        ChecksContext.Equal("趋势线：空输入返回空点集", 0, GaugeTrendMath.BuildPoints([]).Count);
        ChecksContext.Equal("趋势线：无效画布返回空点集", 0, GaugeTrendMath.BuildPoints([1, 2, 3], 0, 24).Count);

        var single = GaugeTrendMath.BuildPoints([42]);
        ChecksContext.Equal("趋势线：单点只画一个点", 1, single.Count);
        CheckApprox("趋势线：单点居中", GaugeTrendMath.CanvasHeight / 2, single[0].Y, 0.001);
        CheckApprox("趋势线：单点 X 在原点", 0, single[0].X, 0.001);

        var flat = GaugeTrendMath.BuildPoints([50, 50, 50]);
        ChecksContext.Equal("趋势线：全平仍是三个点", 3, flat.Count);
        foreach (var point in flat)
        {
            CheckApprox("趋势线：全平点在中线", GaugeTrendMath.CanvasHeight / 2, point.Y, 0.001);
        }
        CheckApprox("趋势线：三点 X 均分 0/60/120", 60, flat[1].X, 0.001);
        CheckApprox("趋势线：末点 X 到右边", GaugeTrendMath.CanvasWidth, flat[2].X, 0.001);

        var rising = GaugeTrendMath.BuildPoints([0, 100]);
        CheckApprox("趋势线：最小值贴底留白", GaugeTrendMath.CanvasHeight - 4, rising[0].Y, 0.001);
        CheckApprox("趋势线：最大值贴顶留白", 4, rising[1].Y, 0.001);

        CheckBuffer();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckBuffer()
    {
        var now = DateTimeOffset.Parse("2026-11-20T12:00:00Z");
        var buffer = new GaugeTrendBuffer();
        ChecksContext.Equal("趋势缓冲：初始为空", 0, buffer.Values().Count);

        buffer.Append(10, now);
        buffer.Append(20, now.AddSeconds(1));
        buffer.Append(30, now.AddSeconds(2));
        ChecksContext.Equal("趋势缓冲：三个采样点", 3, buffer.Values().Count);
        CheckApprox("趋势缓冲：末点值", 30, buffer.Values()[2], 0.001);

        // 同一秒内覆盖最后一个点，不新增。
        buffer.Append(35, now.AddSeconds(2).AddMilliseconds(200));
        ChecksContext.Equal("趋势缓冲：同秒覆盖不新增", 3, buffer.Values().Count);
        CheckApprox("趋势缓冲：覆盖后的末点值", 35, buffer.Values()[2], 0.001);

        // 超过 60 秒的旧点全部淘汰（t=0/1/2 距 t=70 都超过 60 秒），只剩新点。
        buffer.Append(40, now.AddSeconds(70));
        ChecksContext.Equal("趋势缓冲：淘汰过期点后只剩新点", 1, buffer.Values().Count);
        CheckApprox("趋势缓冲：淘汰后首点值", 40, buffer.Values()[0], 0.001);

        buffer.Clear();
        ChecksContext.Equal("趋势缓冲：清空后为空", 0, buffer.Values().Count);
    }

    private static void CheckApprox(string label, double expected, double actual, double tolerance) =>
        ChecksContext.Check(
            label,
            Math.Abs(expected - actual) <= tolerance,
            "期望 " + expected.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "±" + tolerance.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "，实际 " + actual.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
