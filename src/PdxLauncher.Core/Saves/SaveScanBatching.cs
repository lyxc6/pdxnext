namespace PdxLauncher.Core.Saves;

/// <summary>
/// 一批待补细节的粗略行，带它在列表里的起始下标。
/// <para>起始下标是「原位更新」的关键：第二段按 <c>StartIndex + i</c> 回填已有行对象，
/// 不重建表格、不清空集合，滚动位置与已填好的行都不会丢。</para>
/// </summary>
/// <typeparam name="T">粗略行类型。</typeparam>
/// <param name="StartIndex">该批第一项在完整列表中的下标。</param>
/// <param name="Items">该批内容（与完整列表同序）。</param>
public readonly record struct SaveFileBatch<T>(int StartIndex, IReadOnlyList<T> Items);

/// <summary>
/// 第二段的分批规则。纯函数、确定性：同样的输入与批大小必须得到同样的分组，
/// 便于自检钉住边界（空列表、恰好整除、最后一批不满、批大小非法）。
/// </summary>
public static class SaveScanBatching
{
    /// <summary>默认每批条数：4 条既能摊满默认并行度，又能让首屏细节尽快出现。</summary>
    public const int DefaultBatchSize = 4;

    /// <summary>把完整列表按顺序切成固定大小的批次，并为每批记录起始下标。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="items">完整列表；null 抛 <see cref="ArgumentNullException"/>。</param>
    /// <param name="batchSize">每批条数，必须大于 0；默认 <see cref="DefaultBatchSize"/>。</param>
    public static IReadOnlyList<SaveFileBatch<T>> Split<T>(
        IReadOnlyList<T> items,
        int batchSize = DefaultBatchSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "批大小必须大于 0。");
        }

        if (items.Count == 0)
        {
            return [];
        }

        var batchCount = (items.Count + batchSize - 1) / batchSize;
        var batches = new SaveFileBatch<T>[batchCount];
        for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            var start = batchIndex * batchSize;
            var length = Math.Min(batchSize, items.Count - start);
            var slice = new T[length];
            for (var i = 0; i < length; i++)
            {
                slice[i] = items[start + i];
            }

            batches[batchIndex] = new SaveFileBatch<T>(start, slice);
        }

        return batches;
    }
}
