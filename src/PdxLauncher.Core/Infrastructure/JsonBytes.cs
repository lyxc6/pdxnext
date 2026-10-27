namespace PdxLauncher.Core.Infrastructure;

/// <summary>
/// JSON 字节入口：剥掉 UTF-8 BOM 再交给 System.Text.Json。
/// <para>
/// 真机实测：EU5 创意工坊 mod 的 <c>.metadata/metadata.json</c> 全部带 BOM（EF BB BF），
/// 直接 <c>JsonDocument.Parse(byte[])</c> 会抛 <c>'0xEF' is an invalid start of a value</c>，
/// 于是所有 mod 都被跳过、名字回退成工坊 id。凡是从磁盘读原始字节解析 JSON 的地方都该先过这里。
/// </para>
/// </summary>
public static class JsonBytes
{
    private const int BomLength = 3;

    /// <summary>返回去掉 BOM 的视图；没有 BOM 时原样返回。</summary>
    public static ReadOnlyMemory<byte> WithoutBom(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var hasBom = bytes.Length >= BomLength
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF;
        return hasBom ? bytes.AsMemory(BomLength) : bytes;
    }
}
