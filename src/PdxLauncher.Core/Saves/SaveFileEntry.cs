namespace PdxLauncher.Core.Saves;

using PdxLauncher.Core.Games;

/// <summary>
/// 存档文件的「粗略行」：只含目录枚举就能拿到的信息（路径 / 文件名 / 大小 / 文件时间），
/// 不打开文件内容、不计算哈希、不做任何容器解析。
/// <para>
/// 第二段的细节（存档名 / 国家 / 游戏内日期 / 版本 / 是否降级）由
/// <see cref="ISaveGameFileIndex.ParseFile"/> 逐条补齐；本类型只负责把「枚举结果」规范化成
/// 一个不可变、可排序、可比较的值，保证第一段与第二段看到的是同一份文件身份。
/// </para>
/// </summary>
/// <param name="GameId">归一后的游戏 id。</param>
/// <param name="FilePath">文件完整路径（与枚举结果一致，交给解析器原样使用）。</param>
/// <param name="FileName">文件名（不含目录）。</param>
/// <param name="FileSize">文件字节数；读不到属性时为 0。</param>
/// <param name="LastWriteUtc">最后写入时间（UTC）；读不到属性时为 <see cref="DateTimeOffset.MinValue"/>。</param>
/// <param name="Container">该落点的容器形态，供解析器选择读取方式。</param>
public sealed record SaveFileEntry(
    string GameId,
    string FilePath,
    string FileName,
    long FileSize,
    DateTimeOffset LastWriteUtc,
    SaveContainer Container)
{
    /// <summary>
    /// 由枚举到的一条路径构造粗略行；游戏 id 为空、路径为空或文件名推导不出时返回 null。
    /// <para>纯函数：不碰磁盘，大小与时间由调用方给出，便于自检直接构造边界值。</para>
    /// </summary>
    /// <param name="gameId">游戏 id（大小写不敏感，别名会归一）。</param>
    /// <param name="filePath">文件路径。</param>
    /// <param name="fileSize">文件字节数；负数按 0 处理。</param>
    /// <param name="lastWriteUtc">最后写入时间。</param>
    /// <param name="container">容器形态。</param>
    public static SaveFileEntry? TryCreate(
        string gameId,
        string filePath,
        long fileSize,
        DateTimeOffset lastWriteUtc,
        SaveContainer container)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var canonical = KnownGames.CanonicalizeGameId(gameId);
        if (canonical.Length == 0)
        {
            return null;
        }

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return new SaveFileEntry(
            canonical,
            filePath,
            fileName,
            fileSize < 0 ? 0 : fileSize,
            lastWriteUtc,
            container);
    }
}
