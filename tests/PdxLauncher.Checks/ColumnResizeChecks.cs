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
        CheckRejectsUnknownWidth();
        return ChecksContext.Failures - before;
    }

    private static void CheckConstants()
    {
        ChecksContext.Equal("分栏默认宽度 400", 400.0, ColumnResize.DefaultWidth);
        ChecksContext.Equal("分栏左栏下限 280", 280.0, ColumnResize.MinimumWidth);
        ChecksContext.Equal("分栏左栏上限 900", 900.0, ColumnResize.MaximumWidth);
        ChecksContext.Equal("分栏右栏下限 320", 320.0, ColumnResize.MinimumRightWidth);
        ChecksContext.Equal("分隔条列宽 12", 12.0, ColumnResize.SplitterWidth);
    }

    private static void CheckKeepsRequestedWidth()
    {
        // 总宽 1600：900 上限与「1600-12-320=1268」都不限制，用户拖到哪就是哪。
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
        // 总宽 1200：左栏最多 1200-12-320=868，比 900 更紧，必须按总宽收。
        ChecksContext.Equal("总宽 1200 时 5000 收到 868", 868.0, ColumnResize.Clamp(5000.0, 1200.0));
        // 总宽 700：868 与 900 都不适用，上限降到 700-12-320=368。
        ChecksContext.Equal("总宽 700 时 5000 收到 368", 368.0, ColumnResize.Clamp(5000.0, 700.0));
    }

    private static void CheckClampsToLowerBound()
    {
        ChecksContext.Equal("拖负宽度收到 280", 280.0, ColumnResize.Clamp(-200.0, 1600.0));
        ChecksContext.Equal("拖到 100 收到 280", 280.0, ColumnResize.Clamp(100.0, 1600.0));
        // 窗口窄到放不下下限：仍然给下限，宁可右栏挤，也不让左栏变成细条。
        ChecksContext.Equal("总宽 500 时仍给 280", 280.0, ColumnResize.Clamp(400.0, 500.0));
    }

    private static void CheckRejectsUnknownWidth()
    {
        ChecksContext.Equal("NaN 回到默认 400", 400.0, ColumnResize.Clamp(double.NaN, 1600.0));
        ChecksContext.Equal("无穷大回到默认 400", 400.0, ColumnResize.Clamp(double.PositiveInfinity, 1600.0));
        ChecksContext.Equal("总宽未测量时回到默认 400", 400.0, ColumnResize.Clamp(500.0, 0.0));
    }
}
