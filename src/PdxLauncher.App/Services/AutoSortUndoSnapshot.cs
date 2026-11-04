namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 一次自动排序的撤销快照：排序前的完整行列表 + 当时的输入签名。
/// <para>
/// 撤销只在列表仍是排序后的那份内容时成立；若期间增删了 mod、切换了启用或拖动过行，
/// 直接还原旧列表会删掉新加的 mod、复活已删的 mod，因此必须用签名先校验。
/// </para>
/// </summary>
public sealed record AutoSortUndoSnapshot(IReadOnlyList<PlaySetMod> Rows, string Signature)
{
    /// <summary>当前列表是否仍与快照对应的输入一致（可以安全撤销）。</summary>
    public bool IsValidFor(IReadOnlyList<PlaySetMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        return Signature == PlaySetAutoSortService.ComputeInputSignature(mods);
    }
}
