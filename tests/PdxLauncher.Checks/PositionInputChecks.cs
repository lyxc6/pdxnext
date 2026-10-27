namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 位次输入解析自检：<see cref="PositionInput.TryParse"/> 的钳位（超过总数＝置底）、
/// 边界值、以及非法输入的拒绝口径。
/// <para>
/// 起因：播放集只有 4 行时输入 5，界面报「位次无效：请输入 1–4 之间的整数」，
/// 而用户真正想要的是「放到最后一位」。现在向上越界一律钳到 count，
/// 所以「有效数字输入永不被范围挡住」必须由这里钉住——一旦有人把钳位改回硬校验，这里会立刻红。
/// </para>
/// <para>纯内存用例，不读写磁盘，不依赖真实游戏安装。</para>
/// </summary>
public static class PositionInputChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckAboveTotalClampsToEnd();
        CheckInRangeAndTrim();
        CheckRejectsNonPositive();
        CheckRejectsNonAsciiDigits();
        CheckRejectsGarbageAndOverflow();
        CheckRejectsWhenNoRows();
        CheckClampedTargetMovesToLast();
        return ChecksContext.Failures - before;
    }

    private static void CheckAboveTotalClampsToEnd()
    {
        foreach (var text in new[] { "5", "9", "42", "9999" })
        {
            var ok = PositionInput.TryParse(text, 4, out var position);
            ChecksContext.Check("count=4 输入 " + text + " 应判为有效", ok);
            ChecksContext.Equal("count=4 输入 " + text + " 钳到末位 4", 4, position);
        }
    }

    private static void CheckInRangeAndTrim()
    {
        ChecksContext.Check("1 有效", PositionInput.TryParse("1", 4, out var first));
        ChecksContext.Equal("1 → 1", 1, first);

        ChecksContext.Check("4 有效", PositionInput.TryParse("4", 4, out var last));
        ChecksContext.Equal("4 → 4（等于总数不钳）", 4, last);

        ChecksContext.Check("两侧空白被忽略", PositionInput.TryParse("  3  ", 4, out var trimmed));
        ChecksContext.Equal("\"  3  \" → 3", 3, trimmed);

        ChecksContext.Check("单行播放集输入 1 有效", PositionInput.TryParse("1", 1, out var solo));
        ChecksContext.Equal("count=1 输入 1 → 1", 1, solo);

        ChecksContext.Check("单行播放集输入 7 钳到 1", PositionInput.TryParse("7", 1, out var soloBig));
        ChecksContext.Equal("count=1 输入 7 → 1", 1, soloBig);
    }

    private static void CheckRejectsNonPositive()
    {
        // 0 与负数仍判无效：0 既可读作「置顶」也可读作「误触」，不猜。
        foreach (var text in new[] { "0", "-1", "-5" })
        {
            var ok = PositionInput.TryParse(text, 4, out _);
            ChecksContext.Check("输入 " + text + " 应判为无效", !ok);
        }
    }

    private static void CheckRejectsNonAsciiDigits()
    {
        // 中文输入法下这些都可能被敲出来，必须挡在文本层，否则结果不确定。
        foreach (var text in new[] { "２", "３２", "三", "一二三" })
        {
            var ok = PositionInput.TryParse(text, 4, out _);
            ChecksContext.Check("全角/中文数字 \"" + text + "\" 应判为无效", !ok);
        }
    }

    private static void CheckRejectsGarbageAndOverflow()
    {
        foreach (var text in new[] { null, "", "   ", "abc", "3a", "1.5", "1e2", "+2", "99999999999999" })
        {
            var ok = PositionInput.TryParse(text, 4, out _);
            ChecksContext.Check("输入 \"" + (text ?? "<null>") + "\" 应判为无效", !ok);
        }
    }

    private static void CheckRejectsWhenNoRows()
    {
        // 没有行时任何输入都没有意义（防御：真实列表为空时序号入口本就不存在）。
        ChecksContext.Check("count=0 输入 1 判无效", !PositionInput.TryParse("1", 0, out _));
        ChecksContext.Check("count 为负判无效", !PositionInput.TryParse("1", -1, out _));
    }

    private static void CheckClampedTargetMovesToLast()
    {
        // 钳位后的位次必须仍是合法下标，不能落进 PlaySetEditor.Move 的静默 no-op 分支。
        var mods = PlaySetEditor.Renumber(Build(("A", 0), ("B", 0), ("C", 0), ("D", 0)));
        ChecksContext.Check("解析 5（count=4）成功", PositionInput.TryParse("5", 4, out var target));

        // offset = (目标位次 - 1) - 当前索引，与 ModsViewModel.TryMoveModToPosition 里的算法一致。
        var result = PlaySetEditor.Move(mods, 1, (target - 1) - 1);

        ChecksContext.Equal("第 2 行按钳位后的 4 移动 → 落到末位", "A,C,D,B", Names(result));
        ChecksContext.Equal("钳位后 position 仍连续", "0,1,2,3", Positions(result));
    }

    private static List<PlaySetMod> Build(params (string Name, int Position)[] items)
    {
        var result = new List<PlaySetMod>(items.Length);
        foreach (var (name, position) in items)
        {
            result.Add(new PlaySetMod(
                Guid.NewGuid(),
                name,
                "mod/" + name,
                null,
                string.Empty,
                null,
                string.Empty,
                "ready_to_play",
                true,
                position));
        }

        return result;
    }

    private static string Names(IReadOnlyList<PlaySetMod> mods) =>
        string.Join(',', mods.Select(static m => m.Name));

    private static string Positions(IReadOnlyList<PlaySetMod> mods) =>
        string.Join(',', mods.Select(static m => m.Position.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
