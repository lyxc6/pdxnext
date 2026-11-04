namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 自动排序撤销快照的回归护栏：快照只认「应用排序时的那份列表」。
/// 用户随后增删 mod、拖位置或切启用，快照都必须判定失效，
/// 否则撤销会把新加的 mod 删掉、把已删的 mod 复活。
/// 纯内存用例，不读写磁盘。
/// </summary>
public static class AutoSortUndoSnapshotChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckValidForUnchangedList();
        CheckInvalidAfterReorder();
        CheckInvalidAfterEnableToggle();
        CheckInvalidAfterModAdded();
        CheckInvalidAfterModRemoved();

        return ChecksContext.Failures - before;
    }

    /// <summary>列表没动（改名不算动）时可撤销。</summary>
    private static void CheckValidForUnchangedList()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rows = new[]
        {
            Mod(a, "甲", 0, true),
            Mod(b, "乙", 1, false),
        };
        var snapshot = Snapshot(rows);

        ChecksContext.Check("列表未变时可撤销", snapshot.IsValidFor(rows));
        ChecksContext.Check("改名不影响可撤销性", snapshot.IsValidFor(new[]
        {
            Mod(a, "甲改名", 0, true),
            Mod(b, "乙", 1, false),
        }));
    }

    /// <summary>拖了位置后快照失效。</summary>
    private static void CheckInvalidAfterReorder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var snapshot = Snapshot(new[]
        {
            Mod(a, "甲", 0, true),
            Mod(b, "乙", 1, true),
        });

        ChecksContext.Check("位置互换后不可撤销", !snapshot.IsValidFor(new[]
        {
            Mod(a, "甲", 1, true),
            Mod(b, "乙", 0, true),
        }));
    }

    /// <summary>切换启用态后快照失效（启用集会改变加载文件内容）。</summary>
    private static void CheckInvalidAfterEnableToggle()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var snapshot = Snapshot(new[]
        {
            Mod(a, "甲", 0, true),
            Mod(b, "乙", 1, false),
        });

        ChecksContext.Check("切换启用后不可撤销", !snapshot.IsValidFor(new[]
        {
            Mod(a, "甲", 0, true),
            Mod(b, "乙", 1, true),
        }));
    }

    /// <summary>应用排序后新加了 mod（追加到末尾）时快照失效，撤销不能把新 mod 删掉。</summary>
    private static void CheckInvalidAfterModAdded()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var snapshot = Snapshot(new[]
        {
            Mod(a, "甲", 0, true),
            Mod(b, "乙", 1, true),
        });

        ChecksContext.Check("新增 mod 后不可撤销", !snapshot.IsValidFor(new[]
        {
            Mod(a, "甲", 0, true),
            Mod(b, "乙", 1, true),
            Mod(Guid.NewGuid(), "丙", 2, true),
        }));
    }

    /// <summary>删掉一行（后续位置前移）时快照失效，撤销不能把已删的 mod 复活。</summary>
    private static void CheckInvalidAfterModRemoved()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var snapshot = Snapshot(new[]
        {
            Mod(a, "甲", 0, true),
            Mod(b, "乙", 1, true),
        });

        ChecksContext.Check("删除 mod 后不可撤销", !snapshot.IsValidFor(new[]
        {
            Mod(a, "甲", 0, true),
        }));
    }

    private static AutoSortUndoSnapshot Snapshot(IReadOnlyList<PlaySetMod> rows) =>
        new(rows, PlaySetAutoSortService.ComputeInputSignature(rows));

    private static PlaySetMod Mod(Guid id, string name, int position, bool enabled) => new(
        id, name, string.Empty, null, string.Empty, null, string.Empty, string.Empty, enabled, position);
}
