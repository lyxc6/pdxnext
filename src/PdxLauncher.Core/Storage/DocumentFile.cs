namespace PdxLauncher.Core.Storage;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 一份「可整份读写的 JSON 文档」的落点。
/// <para>
/// 设置、会话、缓存索引原本都是 <c>data\*.json</c>：整份读进内存、改完整份写回。
/// 现在这些文档统一搬进自有库（<c>pdxnext.sqlite</c> 的 <c>settings</c>/<c>session</c>/<c>documents</c> 表），
/// 但解析代码一行都不想动 —— 于是把「读/写/删/存在」抽成这一个接口，
/// 调用方只换一个实现，其余逻辑保持原样。
/// </para>
/// <para>
/// 两种实现：<see cref="StoreDocumentFile"/> 走库（生产路径），<see cref="FileDocumentFile"/> 走文件
/// （自检夹具与旧数据导入用）。
/// </para>
/// </summary>
public interface IDocumentFile
{
    /// <summary>给人看的落点描述（日志/排障用），例如 <c>db:settings</c> 或绝对路径。</summary>
    string DisplayPath { get; }

    /// <summary>文档是否存在（读失败也算不存在，由调用方回落到默认值）。</summary>
    bool Exists();

    /// <summary>整份读出；不存在或读失败返回 null（绝不抛）。</summary>
    string? ReadText();

    /// <summary>整份写入；失败返回 false 并已记日志（绝不抛）。</summary>
    bool WriteText(string json);

    /// <summary>删除；本来不存在也算成功。</summary>
    bool Delete();
}

/// <summary>库里的文档：<see cref="IJsonDocumentStore"/> 的一个键。</summary>
public sealed class StoreDocumentFile : IDocumentFile
{
    private readonly IJsonDocumentStore _store;

    /// <summary>绑定一个文档键。</summary>
    public StoreDocumentFile(IJsonDocumentStore store, string key)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _store = store;
        Key = key;
    }

    /// <summary>文档键（<see cref="JsonDocumentKeys"/> 里的常量）。</summary>
    public string Key { get; }

    /// <inheritdoc />
    public string DisplayPath => "db:" + Key;

    /// <inheritdoc />
    public bool Exists() => _store.Exists(Key);

    /// <inheritdoc />
    public string? ReadText() => _store.TryRead(Key, out var json) ? json : null;

    /// <inheritdoc />
    public bool WriteText(string json) => _store.Write(Key, json);

    /// <inheritdoc />
    public bool Delete() => _store.Delete(Key);
}

/// <summary>磁盘上的文档：一个文件路径（兼容旧布局与自检夹具）。</summary>
public sealed class FileDocumentFile : IDocumentFile
{
    private readonly IFileSystem _fileSystem;

    /// <summary>绑定一个文件路径。</summary>
    public FileDocumentFile(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _fileSystem = fileSystem;
        Path = path;
    }

    /// <summary>文件绝对路径。</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string DisplayPath => Path;

    /// <inheritdoc />
    public bool Exists()
    {
        try
        {
            return _fileSystem.FileExists(Path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string? ReadText()
    {
        try
        {
            return _fileSystem.FileExists(Path) ? _fileSystem.ReadAllText(Path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool WriteText(string json)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.EnsureDirectory(directory);
            }

            _fileSystem.WriteAllTextAtomic(Path, json);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool Delete()
    {
        try
        {
            if (_fileSystem.FileExists(Path))
            {
                _fileSystem.Delete(Path);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
