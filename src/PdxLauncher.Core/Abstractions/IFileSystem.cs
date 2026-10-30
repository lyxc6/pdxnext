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

    /// <summary>移动/改名（同卷为原子操作）；目标目录不存在时自动创建。
    /// <para>只对文件有效：目录请用 <see cref="MoveDirectory"/>。</para>
    /// </summary>
    void Move(string source, string destination, bool overwrite);

    /// <summary>给目录改名/移动（同卷为原子操作）；目标父目录不存在时自动创建。</summary>
    /// <remarks>
    /// <see cref="Move"/> 走的是 <c>File.Move</c>，对目录会抛 <see cref="IOException"/>，
    /// 而「本地 mod 目录要改成 ASCII 名」这类操作必须能搬整个目录，所以单独开一个成员，
    /// 免得每个调用点各自猜该用哪个 API。
    /// </remarks>
    void MoveDirectory(string source, string destination);
}
