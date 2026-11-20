namespace PdxLauncher.Core.Infrastructure;

using System.IO.Compression;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 基于 <see cref="ZipFile"/> / <see cref="ZipArchive"/> 的 <see cref="IModArchivePackager"/> 实现。
/// <para>
/// 不用一次性的 <c>ZipFile.CreateFromDirectory</c>：那个 API 没有取消点，大 mod 复制会把 UI 线程
/// 卡住（见 HANDOFF 里主线程同步 IO 卡 39.6s 的记录）。这里逐枚举一个文件检查一次取消，
/// 并把整个打包放进 <see cref="Task.Run"/>，由调用方 ConfigureAwait(true) 回到 UI。
/// </para>
/// </summary>
public sealed class ModArchivePackager : IModArchivePackager
{
    /// <inheritdoc />
    public Task CreateFromDirectory(string sourceDirectory, string archivePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = Path.GetFullPath(sourceDirectory);
                var destination = Path.GetFullPath(archivePath);
                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // tmp + rename：中途失败/取消时不能把半截 zip 留在目标路径被扫描器当成可用 mod。
                var temp = destination + ".pdxnext.tmp";
                try
                {
                    using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        // EnumerateFiles(AllDirectories) 会连 .metadata 这类以 '.' 开头的隐藏目录一起遍历，
                        // 正好满足 metadata 家族封面的相对结构要求；条目统一用 '/' 分隔的 POSIX 路径。
                        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                        }
                    }

                    File.Move(temp, destination, overwrite: true);
                }
                finally
                {
                    TryDeleteTemp(temp);
                }
            },
            cancellationToken);
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
}
