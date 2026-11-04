namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 双列播放集页分栏宽度自检：<see cref="ColumnResize.Clamp"/> 的上下限与总宽联动。
/// <para>
/// 起因：左栏从固定 400 DIP 改成可拖动之后，边界只写在一处页面代码里——
/// 窗口拉窄时如果只看固定上限，右栏会被挤到横向滚动；拖到窗口边缘时如果忘了右栏下限，
/// 启用开关和行尾按钮会直接消失。这里把「总宽怎么限制左栏」的规则钉住。
/// </para>
/// <para>
/// 第二条起因：XAML 的列定义不再写 MinWidth，所以「空间不够时谁让位」全靠这里的钳制函数。
/// 断言里专门锁住「可用宽度不足时左栏先让位、右栏绝不低于下限（除非窗口连分隔条都放不下）」。
/// </para>
/// <para>纯计算用例，不触碰 UI 类型，也不读写磁盘。</para>
/// </summary>
public static class ColumnResizeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckConstants();
        CheckKeepsRequestedWidth();
        CheckClampsToUpperBound();
        CheckForcesRightColumnMinimum();
        CheckClampsToLowerBound();
        CheckYieldsWhenSpaceIsTight();
        CheckRightColumnInvariant();
        CheckRejectsUnknownWidth();
        return ChecksContext.Failures - before;
    }

    private static void CheckConstants()
    {
        ChecksContext.Equal("分栏默认宽度 400", 400.0, ColumnResize.DefaultWidth);
        ChecksContext.Equal("分栏左栏下限 280", 280.0, ColumnResize.MinimumWidth);
        ChecksContext.Equal("分栏左栏上限 900", 900.0, ColumnResize.MaximumWidth);
        ChecksContext.Equal("分栏右栏下限 400", 400.0, ColumnResize.MinimumRightWidth);
        ChecksContext.Equal("分隔条列宽 12", 12.0, ColumnResize.SplitterWidth);
    }

    private static void CheckKeepsRequestedWidth()
    {
        // 总宽 1600：900 上限与「1600-12-400=1188」都不限制，用户拖到哪就是哪。
        ChecksContext.Equal("总宽 1600 时保留 520", 520.0, ColumnResize.Clamp(520.0, 1600.0));
        ChecksContext.Equal("总宽 1600 时保留上限 900", 900.0, ColumnResize.Clamp(900.0, 1600.0));
    }

    private static void CheckClampsToUpperBound()
    {
        // 总宽够大，上限只由 900 决定。
        ChecksContext.Equal("总宽 3000 时 5000 收到 900", 900.0, ColumnResize.Clamp(5000.0, 3000.0));
    }

    private static void CheckForcesRightColumnMinimum()
    {
        // 总宽 1200：左栏最多 1200-12-400=788，比 900 更紧，必须按总宽收。
        ChecksContext.Equal("总宽 1200 时 5000 收到 788", 788.0, ColumnResize.Clamp(5000.0, 1200.0));
        // 用户截图里的尺寸：双列区 909 DIP 时左栏最多 909-12-400=497。
        ChecksContext.Equal("总宽 909 时 900 收到 497", 497.0, ColumnResize.Clamp(900.0, 909.0));
        // 总宽 700：上限降到 700-12-400=288。
        ChecksContext.Equal("总宽 700 时 5000 收到 288", 288.0, ColumnResize.Clamp(5000.0, 700.0));
    }

    private static void CheckClampsToLowerBound()
    {
        ChecksContext.Equal("拖负宽度收到 280", 280.0, ColumnResize.Clamp(-200.0, 1600.0));
        ChecksContext.Equal("拖到 100 收到 280", 280.0, ColumnResize.Clamp(100.0, 1600.0));
        // 1040 DIP 最小窗口：双列区 1040-216-32=792，上限 792-12-400=380，仍高于下限。
        ChecksContext.Equal("总宽 792（最小窗口）时 400 收到 380", 380.0, ColumnResize.Clamp(400.0, 792.0));
    }

    private static void CheckYieldsWhenSpaceIsTight()
    {
        // 可用宽度放不下「左栏下限 + 分隔条 + 右栏下限」时，左栏必须让位，不能硬顶到 280 把右栏挤没。
        ChecksContext.Equal("总宽 552 时左栏让位到 140", 140.0, ColumnResize.Clamp(400.0, 552.0));
        ChecksContext.Equal("总宽 500 时左栏让位到 88", 88.0, ColumnResize.Clamp(400.0, 500.0));
        ChecksContext.Equal("总宽 400 时左栏让到 0", 0.0, ColumnResize.Clamp(5000.0, 400.0));
        // 让位的宽度不能被「下限」顶回去（这正是 XAML 里 MinWidth 会造成的溢出）。
        ChecksContext.Check(
            "总宽 552 时左栏低于常规下限",
            ColumnResize.Clamp(400.0, 552.0) < ColumnResize.MinimumWidth,
            $"实际 {ColumnResize.Clamp(400.0, 552.0):0.#}");
    }

    private static void CheckRightColumnInvariant()
    {
        // 任何可用宽度下，右栏 = 总宽 - 分隔条 - 左栏，且不得低于「400 与 总宽-分隔条 中的较小者」。
        double[] available = [400.0, 500.0, 552.0, 700.0, 792.0, 909.0, 1200.0, 1800.0, 3000.0];
        foreach (var total in available)
        {
            var left = ColumnResize.Clamp(5000.0, total);
            var right = total - ColumnResize.SplitterWidth - left;
            var floor = Math.Min(ColumnResize.MinimumRightWidth, total - ColumnResize.SplitterWidth);
            ChecksContext.Check(
                $"总宽 {total:0.#} 时右栏不低于保底宽",
                right >= floor - 0.5,
                $"右栏 {right:0.#}，保底 {floor:0.#}，左栏 {left:0.#}");
            ChecksContext.Check(
                $"总宽 {total:0.#} 时左右两栏不溢出",
                left + ColumnResize.SplitterWidth + right <= total + 0.5,
                $"左 {left:0.#} + 条 {ColumnResize.SplitterWidth:0.#} + 右 {right:0.#}");
        }
    }

    private static void CheckRejectsUnknownWidth()
    {
        ChecksContext.Equal("NaN 回到默认 400", 400.0, ColumnResize.Clamp(double.NaN, 1600.0));
        ChecksContext.Equal("无穷大回到默认 400", 400.0, ColumnResize.Clamp(double.PositiveInfinity, 1600.0));
        ChecksContext.Equal("总宽未测量时回到默认 400", 400.0, ColumnResize.Clamp(500.0, 0.0));
        ChecksContext.Equal("总宽为 NaN 时回到默认 400", 400.0, ColumnResize.Clamp(500.0, double.NaN));
    }
}
