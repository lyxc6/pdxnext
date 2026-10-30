namespace PdxLauncher.Core.Storage;

using System.Globalization;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 一次性迁移：把数据根目录里遗留的整份 JSON 文件搬进自有库，原文件改名留档。
/// <para>
/// 这些文件是「数据全部入库」之前的形态（<c>settings.json</c>、<c>pdx-session.json</c> 与 6 份缓存索引）。
/// 迁移只做一次：搬完把原文件改名成 <c>&lt;名字&gt;.migrated.bak</c>，所以重复执行不会重复导入。
/// 库里已经有同名键时以库为准，只把旧文件改名留档，绝不覆盖库里的新数据。
/// </para>
/// </summary>
public sealed class LegacyJsonImporter
{
    /// <summary>迁移完成后写入的 meta 键（值是导入计数与时间戳）。</summary>
    public const string MarkerKey = "legacy-json-imported";

    /// <summary>原文件迁移后的后缀。</summary>
    public const string BackupSuffix = ".migrated.bak";

    private const string ThemeAssetsSuffix = ".assets.json";

    private static readonly (string FileName, string Key)[] FixedMappings =
    [
        ("settings.json", JsonDocumentKeys.Settings),
        ("pdx-session.json", JsonDocumentKeys.Session),
        ("dlc-descriptions.json", JsonDocumentKeys.DlcDescriptions),
        ("news-cards.json", JsonDocumentKeys.NewsCards),
        ("mod-thumbnails.json", JsonDocumentKeys.ModThumbnails),
        ("workshop-pages.json", JsonDocumentKeys.WorkshopPages),
        ("workshop-info.json", JsonDocumentKeys.WorkshopInfo),
    ];

    private readonly PdxDatabase _database;
    private readonly IFileSystem _fileSystem;
    private readonly IJsonDocumentStore _documents;
    private readonly IAppLogger _logger;

    /// <summary>创建导入器。</summary>
    public LegacyJsonImporter(
        PdxDatabase database,
        IFileSystem fileSystem,
        IJsonDocumentStore documents,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(logger);
        _database = database;
        _fileSystem = fileSystem;
        _documents = documents;
        _logger = logger;
    }

    /// <summary>
    /// 扫描 <paramref name="dataRoot"/> 并把遗留 JSON 导入自有库。
    /// </summary>
    /// <returns>真正写入库的文件数（已存在而只改名的不计）。</returns>
    public int Import(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !_fileSystem.DirectoryExists(dataRoot))
        {
            return 0;
        }

        var imported = 0;
        var touched = 0;

        foreach (var (fileName, key) in FixedMappings)
        {
            var path = _fileSystem.Combine(dataRoot, fileName);
            if (!_fileSystem.FileExists(path))
            {
                continue;
            }

            touched++;
            if (ImportOne(path, key))
            {
                imported++;
            }
        }

        foreach (var path in EnumerateThemeAssets(dataRoot))
        {
            var gameId = ReadThemeGameId(path);
            if (gameId.Length == 0)
            {
                continue;
            }

            touched++;
            if (ImportOne(path, JsonDocumentKeys.ThemeAssets(gameId)))
            {
                imported++;
            }
        }

        if (touched > 0)
        {
            _database.WriteMeta(
                MarkerKey,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) +
                ";files=" + touched.ToString(CultureInfo.InvariantCulture) +
                ";imported=" + imported.ToString(CultureInfo.InvariantCulture));
            _logger.Info("遗留 JSON 已入库：" + imported.ToString(CultureInfo.InvariantCulture) + " 份（扫描 " +
                         touched.ToString(CultureInfo.InvariantCulture) + " 份）");
        }

        return imported;
    }

    private bool ImportOne(string path, string key)
    {
        var imported = false;
        try
        {
            if (_documents.Exists(key))
            {
                // 库里已有更新的一份：旧文件只留档，不回写。
                _logger.Info("自有库已有 " + key + "，遗留文件仅留档：" + _fileSystem.GetFileName(path));
            }
            else
            {
                var text = _fileSystem.ReadAllText(path);
                imported = _documents.Write(key, text);
                if (!imported)
                {
                    _logger.Warn("遗留文件入库失败，保留原文件：" + path);
                    return false;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("读遗留文件失败，保留原文件：" + path + "（" + exception.Message + "）");
            return false;
        }

        try
        {
            _fileSystem.Move(path, path + BackupSuffix, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 改名失败不影响数据（已在库里），下次启动会再走一遍「已存在 → 留档」分支。
            _logger.Warn("遗留文件改名留档失败：" + path + "（" + exception.Message + "）");
        }

        return imported;
    }

    private IEnumerable<string> EnumerateThemeAssets(string dataRoot)
    {
        try
        {
            return _fileSystem.EnumerateFiles(dataRoot, "*" + ThemeAssetsSuffix, recursive: false).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("扫描主题缓存文件失败：" + exception.Message);
            return Array.Empty<string>();
        }
    }

    private string ReadThemeGameId(string path)
    {
        var name = _fileSystem.GetFileName(path);
        return name.Length > ThemeAssetsSuffix.Length &&
               name.EndsWith(ThemeAssetsSuffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^ThemeAssetsSuffix.Length]
            : string.Empty;
    }
}
