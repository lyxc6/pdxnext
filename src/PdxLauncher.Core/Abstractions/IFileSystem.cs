namespace PdxLauncher.Core.Abstractions;

/// <summary>文件系统抽象；所有实现必须保证写操作是 tmp+rename 原子写。</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    /// <summary>文件字节数；文件不存在（或读不到属性）返回 0，不抛异常。</summary>
    long GetFileSize(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    byte[] ReadAllBytes(string path);

    void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> data);

    void WriteAllTextAtomic(string path, string text);

    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive);

    IEnumerable<string> EnumerateDirectories(string directory);

    string Combine(params string[] parts);

    string GetFullPath(string path);

    string? GetDirectoryName(string path);

    string GetFileName(string path);

    void EnsureDirectory(string path);

    void Delete(string path);

    void Copy(string source, string destination, bool overwrite);
}
