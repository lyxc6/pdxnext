namespace PdxLauncher.Core.Infrastructure;

using System.Text;

/// <summary>基于 System.IO 的 IFileSystem 实现；写操作一律 tmp+rename 原子写。</summary>
public sealed class FileSystem : Abstractions.IFileSystem
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public long GetFileSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public string ReadAllText(string path) => File.ReadAllText(path, Utf8NoBom);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public Stream OpenRead(string path) => new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 4096,
        FileOptions.None);

    public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".pdxnext.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            // 失败时不能把半截临时文件留在目录里：上层只看「有没有 .pdxnext.tmp」判断写没写成功。
            TryDeleteTemp(temp);
        }
    }

    private static void TryDeleteTemp(string temp)
    {
        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void WriteAllTextAtomic(string path, string text) =>
        WriteAllBytesAtomic(path, Utf8NoBom.GetBytes(text ?? string.Empty));

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        try
        {
            return Directory.EnumerateFiles(directory, searchPattern, option).ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public string Combine(params string[] parts) => Path.Combine(parts ?? Array.Empty<string>());

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);

    public string GetFileName(string path) => Path.GetFileName(path);

    public void EnsureDirectory(string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public void Copy(string source, string destination, bool overwrite)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(source, destination, overwrite);
    }

    public void Move(string source, string destination, bool overwrite)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(source, destination, overwrite);
    }

    public void MoveDirectory(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Directory.Move(source, destination);
    }
}
