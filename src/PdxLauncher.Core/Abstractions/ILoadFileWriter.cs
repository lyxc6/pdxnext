using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>加载文件（dlc_load.json / content_load.json）读写。</summary>
public interface ILoadFileWriter
{
    /// <summary>序列化但不落盘，供字节级 golden 对比使用。</summary>
    string Serialize(GameInstallation game, LoadFileContent content);

    /// <summary>读取现有加载文件；不存在或损坏时返回空内容。</summary>
    Task<LoadFileContent> ReadAsync(GameInstallation game, CancellationToken cancellationToken);

    /// <summary>原子写入加载文件。</summary>
    Task WriteAsync(GameInstallation game, LoadFileContent content, CancellationToken cancellationToken);
}
