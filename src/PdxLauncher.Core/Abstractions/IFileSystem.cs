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

    /// <summary>
    /// 以只读方式打开文件流，供需要随机访问的容器解析（zip）和有限头读取使用。
    /// <para>调用方负责释放返回的流；实现应允许其它进程共享读写。</para>
    /// </summary>
    Stream OpenRead(string path);

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

    /// <summary>
    /// 递归复制整个目录树（含以 <c>.</c> 开头的隐藏目录，如 <c>.metadata</c>）。
    /// <para>目标目录不存在时自动创建；<paramref name="overwrite"/> 为 false 且目标文件已存在时抛 <see cref="IOException"/>。</para>
    /// <para>与 <see cref="MoveDirectory"/> 成对：CopyDirectory 复制整棵树，<see cref="Copy"/> 只复制单个文件。</para>
    /// <para><paramref name="cancellationToken"/> 在进入子目录前与复制每个文件前检查；取消时抛
    /// <see cref="OperationCanceledException"/>，已复制的部分由调用方负责清理（staging 即为此设计）。</para>
    /// </summary>
    void CopyDirectory(string source, string destination, bool overwrite, CancellationToken cancellationToken = default);

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
