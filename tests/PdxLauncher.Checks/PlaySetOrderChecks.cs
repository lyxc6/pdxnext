namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 播放集排序自检：<see cref="PlaySetEditor.Renumber"/> 与 <see cref="PlaySetEditor.Move"/> 的
/// position 连续性、插入语义（其余行依次顺移而非交换）、越界与非法索引的兜底行为。
/// <para>
/// 这是「播放集页位次就地编辑」功能的回归护栏：位次输入框最终走的就是
/// <c>PlaySetEditor.Move(mods, index, targetIndex - index)</c>，
/// 一旦有人把 Move 改成「交换」或让 position 出现空洞/重复，这里会立刻红。
/// </para>
/// <para>纯内存用例，不读写磁盘，不依赖真实游戏安装。</para>
/// </summary>
public static class PlaySetOrderChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckRenumber();
        CheckMoveProducesContiguousPositions();
        CheckMoveIsInsertNotSwap();
        CheckMoveToFirstAndLast();
        CheckMoveOutOfRangeIsNoOpButRenumbered();
        CheckMovePreservesOtherFields();
        return ChecksContext.Failures - before;
    }

    private static void CheckRenumber()
    {
        var mods = Build(("A", 5), ("B", 9), ("C", 42));
        var result = PlaySetEditor.Renumber(mods);

        ChecksContext.Equal("Renumber 后 position 连续 0..n-1", "0,1,2", Positions(result));
        ChecksContext.Equal("Renumber 不改变条目顺序", "A,B,C", Names(result));
    }

    private static void CheckMoveProducesContiguousPositions()
    {
        var mods = PlaySetEditor.Renumber(Build(("A", 0), ("B", 0), ("C", 0), ("D", 0), ("E", 0)));
        var result = PlaySetEditor.Move(mods, 0, 2);

        ChecksContext.Equal("Move 后 position 仍连续 0..n-1", "0,1,2,3,4", Positions(result));
        ChecksContext.Equal("Move 后顺序正确", "B,C,A,D,E", Names(result));
    }

    private static void CheckMoveIsInsertNotSwap()
    {
        // 第 5 位 → 第 2 位（0 基 index 4 → index 1）：是插入，2/3/4 依次后移，不是与第 2 位互换。
        var mods = PlaySetEditor.Renumber(
            Build(("A", 0), ("B", 0), ("C", 0), ("D", 0), ("E", 0)));
        var result = PlaySetEditor.Move(mods, 4, 1 - 4);

        ChecksContext.Equal("跳位是插入语义（其余行依次顺移）", "A,E,B,C,D", Names(result));
        ChecksContext.Equal("跳位后 position 连续", "0,1,2,3,4", Positions(result));
    }

    private static void CheckMoveToFirstAndLast()
    {
        var mods = PlaySetEditor.Renumber(Build(("A", 0), ("B", 0), ("C", 0)));

        var toFirst = PlaySetEditor.Move(mods, 2, 0 - 2);
        ChecksContext.Equal("移到首位", "C,A,B", Names(toFirst));

        var toLast = PlaySetEditor.Move(mods, 0, 2 - 0);
        ChecksContext.Equal("移到末位", "B,C,A", Names(toLast));
    }

    private static void CheckMoveOutOfRangeIsNoOpButRenumbered()
    {
        var mods = PlaySetEditor.Renumber(Build(("A", 0), ("B", 0), ("C", 0)));

        // 越界索引与越界目标都必须「不抛异常、不变顺序、但 position 归一」——
        // 位次输入框在调用前已把 1..count 之外的输入挡掉，这里的兜底是第二道防线。
        ChecksContext.Equal("index 越界时顺序不变", "A,B,C", Names(PlaySetEditor.Move(mods, 9, 1)));
        ChecksContext.Equal("index 越界时 position 归一", "0,1,2", Positions(PlaySetEditor.Move(mods, 9, 1)));
        ChecksContext.Equal("target 越界时顺序不变", "A,B,C", Names(PlaySetEditor.Move(mods, 0, 99)));
        ChecksContext.Equal("target 越界时 position 归一", "0,1,2", Positions(PlaySetEditor.Move(mods, 0, -99)));
    }

    private static void CheckMovePreservesOtherFields()
    {
        var mods = new List<PlaySetMod>
        {
            Make("A", 0, enabled: true, gameRegistryId: "reg/a"),
            Make("B", 1, enabled: false, gameRegistryId: "reg/b"),
        };

        var result = PlaySetEditor.Move(mods, 0, 1);

        ChecksContext.Equal("Move 后条目顺序改变", "B,A", Names(result));
        ChecksContext.Check("Move 只改 position，不动 Enabled", result[1].Enabled && !result[0].Enabled);
        ChecksContext.Check("Move 只改 position，不动 GameRegistryId", result[1].GameRegistryId == "reg/a");
        ChecksContext.Check("Move 保留 ModId", result[1].ModId == mods[0].ModId);
    }

    private static PlaySetMod Make(
        string name,
        int position,
        bool enabled = true,
        string gameRegistryId = "")
    {
        return new PlaySetMod(
            Guid.NewGuid(),
            name,
            "mod/" + name,
            null,
            gameRegistryId,
            null,
            string.Empty,
            "ready_to_play",
            enabled,
            position);
    }

    private static List<PlaySetMod> Build(params (string Name, int Position)[] items)
    {
        var result = new List<PlaySetMod>(items.Length);
        foreach (var (name, position) in items)
        {
            result.Add(Make(name, position));
        }

        return result;
    }

    private static string Names(IReadOnlyList<PlaySetMod> mods) =>
        string.Join(',', mods.Select(static m => m.Name));

    private static string Positions(IReadOnlyList<PlaySetMod> mods) =>
        string.Join(',', mods.Select(static m => m.Position.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
