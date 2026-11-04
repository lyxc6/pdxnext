namespace PdxLauncher.Core.Mods.Sorting;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 从 mod 目录读 <c>.metadata/metadata.json</c>（V3 / EU5 家族）并交给 <see cref="ModMetadataParser"/>。
/// </summary>
public static class ModMetadataReader
{
    /// <summary>元数据文件名。</summary>
    public const string MetadataFileName = "metadata.json";

    /// <summary>
    /// 读不到（目录/文件不存在、IO 失败）时返回 false 并给出空描述。
    /// 路径来自播放集行，可能指向已被删掉的目录，因此缺目录是常态而非错误。
    /// </summary>
    public static bool TryRead(
        IFileSystem fileSystem,
        string? dirPath,
        IAppLogger logger,
        out ModMetadataDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        descriptor = ModMetadataDescriptor.Empty;
        if (string.IsNullOrWhiteSpace(dirPath) || !fileSystem.DirectoryExists(dirPath!))
        {
            return false;
        }

        var path = fileSystem.Combine(dirPath!, ".metadata", MetadataFileName);
        if (!fileSystem.FileExists(path))
        {
            return false;
        }

        try
        {
            // EU5 的 metadata.json 真机全部带 BOM，直接交给 JsonDocument 会解析失败。
            descriptor = ModMetadataParser.Parse(JsonBytes.WithoutBom(fileSystem.ReadAllBytes(path)));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warn("读取 mod 元数据失败，已跳过：" + path + "（" + exception.Message + "）");
            return false;
        }
    }
}
