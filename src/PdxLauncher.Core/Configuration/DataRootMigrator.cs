namespace PdxLauncher.Core.Configuration;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 把旧位置 <c>%LOCALAPPDATA%\PdxNext</c> 里的设置与图片缓存复制到程序目录下的 data\。
/// <para>
/// 只读旧目录、只写新目录、<b>永不删除源目录</b>（清理入口在设置页，需用户确认）。
/// 幂等：目标已有 settings.json 或迁移标记时直接跳过。任何 IO 失败都只记 Warn 并中止本次迁移，
/// 不写标记，下次启动会重试。
/// </para>
/// </summary>
public sealed class DataRootMigrator
{
    /// <summary>迁移完成后写入 target 的标记文件。</summary>
    public const string MarkerFileName = ".migrated-from-localappdata";

    private static readonly string[] SingleFiles = { "settings.json", "dlc-descriptions.json" };

    private static readonly string[] DirectoryNames = { "theme-cache", "news-cache", "mod-cache" };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly string _sourceRoot;
    private readonly string _targetRoot;

    public DataRootMigrator(IFileSystem fileSystem, IAppLogger logger, string sourceRoot, string targetRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        _fileSystem = fileSystem;
        _logger = logger;
        _sourceRoot = sourceRoot;
        _targetRoot = targetRoot;
    }

    /// <summary>旧数据位置（只读）。</summary>
    public static string LegacyRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PdxNext");

    public DataMigrationResult Migrate()
    {
        if (string.Equals(_sourceRoot, _targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new DataMigrationResult(false, 0, 0, null);
        }

        try
        {
            if (!_fileSystem.DirectoryExists(_sourceRoot))
            {
                return new DataMigrationResult(false, 0, 0, null);
            }

            if (_fileSystem.FileExists(_fileSystem.Combine(_targetRoot, MarkerFileName)) ||
                _fileSystem.FileExists(_fileSystem.Combine(_targetRoot, "settings.json")))
            {
                return new DataMigrationResult(false, 0, 0, null);
            }

            _fileSystem.EnsureDirectory(_targetRoot);

            var files = 0;
            var bytes = 0L;

            foreach (var name in SingleFiles)
            {
                var source = _fileSystem.Combine(_sourceRoot, name);
                if (!_fileSystem.FileExists(source))
                {
                    continue;
                }

                files += CopyFile(source, _fileSystem.Combine(_targetRoot, name), ref bytes);
            }

            foreach (var name in DirectoryNames)
            {
                var sourceDirectory = _fileSystem.Combine(_sourceRoot, name);
                if (!_fileSystem.DirectoryExists(sourceDirectory))
                {
                    continue;
                }

                foreach (var source in _fileSystem.EnumerateFiles(sourceDirectory, "*", recursive: true))
                {
                    var relative = Path.GetRelativePath(sourceDirectory, source);
                    files += CopyFile(source, _fileSystem.Combine(_targetRoot, name, relative), ref bytes);
                }
            }

            _fileSystem.WriteAllTextAtomic(
                _fileSystem.Combine(_targetRoot, MarkerFileName),
                DateTimeOffset.Now.ToString("O") + Environment.NewLine +
                "files=" + files.ToString(System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine +
                "source=" + _sourceRoot + Environment.NewLine);

            _logger.Info("数据目录迁移完成：从 " + _sourceRoot + " 复制 " + files.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个文件（" + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B）");
            return new DataMigrationResult(true, files, bytes, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.Warn("数据目录迁移失败（旧数据保留在 " + _sourceRoot + "）：" + e.Message);
            return new DataMigrationResult(false, 0, 0, e.Message);
        }
    }

    private int CopyFile(string source, string destination, ref long bytes)
    {
        if (_fileSystem.FileExists(destination))
        {
            return 0;
        }

        var size = SizeOf(source);
        _fileSystem.Copy(source, destination, overwrite: false);
        bytes += size;
        return 1;
    }

    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return 0;
        }
    }
}
