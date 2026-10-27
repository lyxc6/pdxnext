namespace PdxLauncher.Core.Playsets;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Parsing;

/// <summary>
/// <see cref="IModCatalog"/> 实现：把"磁盘上的 mod 描述文件"与"launcher-v2.sqlite 的 mods 表"合并成一份目录。
/// <para>CK3/Stellaris：扫 &lt;gameDataDir&gt;\mod\*.mod（Clausewitz 解析）。</para>
/// <para>V3：扫 workshop 目录 &lt;...&gt;\workshop\content\&lt;appid&gt;\*\\.metadata\\metadata.json，
/// 以及 &lt;gameDataDir&gt;\mod\*\\.metadata\\metadata.json（本地 mod）。</para>
/// <para>合并键：gameRegistryId（如 mod/ugc_2218867072.mod）→ dirPath（绝对化后大小写不敏感）→ steamId。</para>
/// </summary>
public sealed class ModCatalog : IModCatalog
{
    private const string ModDescriptorPattern = "*.mod";
    private const string MetadataFileName = "metadata.json";
    private const string MetadataDirectoryName = ".metadata";
    private const string WorkshopSegment = "workshop";
    private const string ContentSegment = "content";

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly ModThumbnailResolver _thumbnails;

    /// <summary>创建 mod 目录。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="thumbnails">缩略图解析器；为 null 时自行创建（Steam 库列表延迟读取）。</param>
    public ModCatalog(IFileSystem fileSystem, IAppLogger logger, ModThumbnailResolver? thumbnails = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
        _thumbnails = thumbnails ?? new ModThumbnailResolver(fileSystem, logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Task.Run(() => Discover(game, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<PlaySetMod> Discover(GameInstallation game, CancellationToken cancellationToken)
    {
        var sqliteRows = ReadSqliteMods(game);
        var discovered = new List<ModRecord>();

        ScanModDescriptors(game, discovered, cancellationToken);
        ScanMetadataFiles(game, discovered, cancellationToken);

        // 合并：先按 sqlite 行匹配，未匹配的 sqlite 行也纳入结果（例如目录已删但仍登记在库里）。
        var usedSqlite = new HashSet<int>();
        var merged = new List<PlaySetMod>(discovered.Count + sqliteRows.Count);

        foreach (var record in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matchIndex = FindSqliteMatch(sqliteRows, record);
            ModRecord? sqlite = null;
            if (matchIndex >= 0)
            {
                usedSqlite.Add(matchIndex);
                sqlite = sqliteRows[matchIndex];
            }

            merged.Add(Compose(game, record, sqlite, merged.Count));
        }

        for (var i = 0; i < sqliteRows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (usedSqlite.Contains(i))
            {
                continue;
            }

            merged.Add(Compose(game, record: null, sqliteRows[i], merged.Count));
        }

        merged.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.ModId.CompareTo(right.ModId);
        });

        // 排序后 position 需要重排，保证调用方看到的位置是连续且稳定的。
        var normalized = new List<PlaySetMod>(merged.Count);
        for (var i = 0; i < merged.Count; i++)
        {
            normalized.Add(merged[i] with { Position = i });
        }

        _logger.Info(
            "mod 扫描完成：" + game.GameId + "，磁盘描述文件 " +
            discovered.Count.ToString(CultureInfo.InvariantCulture) + " 个，sqlite mods 表 " +
            sqliteRows.Count.ToString(CultureInfo.InvariantCulture) + " 行，合并后 " +
            normalized.Count.ToString(CultureInfo.InvariantCulture) + " 个。");
        return normalized;
    }

    // ---------------------------------------------------------------- 磁盘扫描

    private void ScanModDescriptors(GameInstallation game, List<ModRecord> discovered, CancellationToken cancellationToken)
    {
        var modDir = game.ModDir;
        if (string.IsNullOrWhiteSpace(modDir) || !_fileSystem.DirectoryExists(modDir))
        {
            return;
        }

        var files = _fileSystem.EnumerateFiles(modDir, ModDescriptorPattern, recursive: false).ToList();
        files.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = _fileSystem.ReadAllText(file);
                var values = ClausewitzParser.Parse(text);
                var registryId = "mod/" + _fileSystem.GetFileName(file);
                var dirPath = PathHelper.ResolveMaybeRelative(game.GameDataDir, Read(values, "path"));
                var archivePath = PathHelper.ResolveMaybeRelative(game.GameDataDir, Read(values, "archive"));

                discovered.Add(new ModRecord(
                    Name: Read(values, "name"),
                    DirPath: dirPath,
                    ArchivePath: archivePath,
                    GameRegistryId: registryId,
                    SteamId: Read(values, "remote_file_id"),
                    RequiredVersion: Read(values, "supported_version"),
                    ThumbnailPath: null,
                    Status: null,
                    Picture: Read(values, "picture")));
            }
            catch (IOException exception)
            {
                _logger.Warn("读取 .mod 描述文件失败，已跳过：" + file + "：" + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.Warn("读取 .mod 描述文件无权限，已跳过：" + file + "：" + exception.Message);
            }
        }
    }

    private void ScanMetadataFiles(GameInstallation game, List<ModRecord> discovered, CancellationToken cancellationToken)
    {
        // 1) workshop：<...>/workshop/content/<appid>/<modId>/.metadata/metadata.json
        foreach (var workshopDirectory in EnumerateWorkshopDirectories(game))
        {
            if (!_fileSystem.DirectoryExists(workshopDirectory))
            {
                continue;
            }

            foreach (var modDirectory in _fileSystem.EnumerateDirectories(workshopDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMetadataMod(game, modDirectory, discovered);
            }
        }

        // 2) 本地 mod：<gameDataDir>/mod/<dir>/.metadata/metadata.json
        var localModRoot = game.ModDir;
        if (!string.IsNullOrWhiteSpace(localModRoot) && _fileSystem.DirectoryExists(localModRoot))
        {
            foreach (var modDirectory in _fileSystem.EnumerateDirectories(localModRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMetadataMod(game, modDirectory, discovered);
            }
        }
    }

    private void AddMetadataMod(GameInstallation game, string modDirectory, List<ModRecord> discovered)
    {
        var metadataPath = Path.Combine(modDirectory, MetadataDirectoryName, MetadataFileName);
        if (!_fileSystem.FileExists(metadataPath))
        {
            return;
        }

        try
        {
            var rawBytes = _fileSystem.ReadAllBytes(metadataPath);
            using var document = JsonDocument.Parse(rawBytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.Warn("mod metadata.json 根节点不是对象，已跳过：" + metadataPath);
                return;
            }

            var name = ReadJsonProperty(root, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                // 真机实测有 2 个 mod 的 metadata.json name 为空，回退成目录名，避免出现无名条目。
                name = _fileSystem.GetFileName(modDirectory);
            }

            var steamId = ReadJsonProperty(root, "id");
            if (string.IsNullOrWhiteSpace(steamId))
            {
                // id 为空时用 workshop 目录名（就是 Steam 创意工坊 id）兜底。
                steamId = _fileSystem.GetFileName(modDirectory);
            }

            discovered.Add(new ModRecord(
                Name: name,
                DirPath: modDirectory,
                ArchivePath: null,
                GameRegistryId: string.Empty,
                SteamId: steamId,
                RequiredVersion: ReadJsonProperty(root, "supported_game_version"),
                ThumbnailPath: null,
                Status: null,
                Picture: ReadJsonProperty(root, "thumbnail")?.Trim()));
        }
        catch (JsonException exception)
        {
            _logger.Warn("mod metadata.json 解析失败，已跳过：" + metadataPath + "：" + exception.Message);
        }
        catch (IOException exception)
        {
            _logger.Warn("读取 mod metadata.json 失败，已跳过：" + metadataPath + "：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("读取 mod metadata.json 无权限，已跳过：" + metadataPath + "：" + exception.Message);
        }
    }

    /// <summary>
    /// 推导 workshop 的 content 目录。优先用 InstallRoot 的固定相对结构
    /// （&lt;library&gt;\steamapps\common\&lt;dir&gt; → &lt;library&gt;\steamapps\workshop\content\&lt;appid&gt;），
    /// 再用 sqlite 里已登记的 dirPath 反推（防止库路径与安装路径不同源）。
    /// </summary>
    private IReadOnlyList<string> EnumerateWorkshopDirectories(GameInstallation game)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(game.InstallRoot))
        {
            try
            {
                var steamApps = Path.GetFullPath(Path.Combine(game.InstallRoot, "..", ".."));
                var contentRoot = Path.Combine(steamApps, WorkshopSegment, ContentSegment);
                if (game.SteamAppId is { } appId)
                {
                    candidates.Add(Path.Combine(contentRoot, appId.ToString(CultureInfo.InvariantCulture)));
                }
                else if (_fileSystem.DirectoryExists(contentRoot))
                {
                    foreach (var directory in _fileSystem.EnumerateDirectories(contentRoot))
                    {
                        candidates.Add(directory);
                    }
                }
            }
            catch (ArgumentException exception)
            {
                _logger.Warn("推导 workshop 目录失败：" + exception.Message);
            }
            catch (NotSupportedException exception)
            {
                _logger.Warn("推导 workshop 目录失败：" + exception.Message);
            }
        }

        // 从 sqlite 的 dirPath 反推：<content>/<appid>/<modId> 的父目录就是 <appid> 目录。
        foreach (var row in ReadSqliteMods(game))
        {
            var dirPath = row.DirPath;
            if (string.IsNullOrWhiteSpace(dirPath))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(dirPath);
            if (string.IsNullOrEmpty(parent))
            {
                continue;
            }

            if (parent.Contains(WorkshopSegment + Path.DirectorySeparatorChar + ContentSegment, StringComparison.OrdinalIgnoreCase)
                || parent.Replace('/', Path.DirectorySeparatorChar).Contains(
                    WorkshopSegment + Path.DirectorySeparatorChar + ContentSegment,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!candidates.Contains(parent, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(parent);
                }
            }
        }

        return candidates;
    }

    // ---------------------------------------------------------------- sqlite

    private List<ModRecord> ReadSqliteMods(GameInstallation game)
    {
        var databasePath = PlaySetRepository.GetDatabasePath(game);
        if (!_fileSystem.FileExists(databasePath))
        {
            return new List<ModRecord>();
        }

        try
        {
            using var connection = SqliteConnection.OpenReadOnly(databasePath);
            var rows = connection.Query(
                "SELECT id, displayName, dirPath, archivePath, gameRegistryId, steamId, thumbnailPath, " +
                "requiredVersion, status FROM mods");

            var result = new List<ModRecord>(rows.Count);
            foreach (var row in rows)
            {
                var id = PlaySetRepository.ReadNullableString(row, "id");
                result.Add(new ModRecord(
                    Name: PlaySetRepository.ReadString(row, "displayName"),
                    DirPath: PlaySetRepository.ReadNullableString(row, "dirPath"),
                    ArchivePath: PlaySetRepository.ReadNullableString(row, "archivePath"),
                    GameRegistryId: PlaySetRepository.ReadString(row, "gameRegistryId"),
                    SteamId: PlaySetRepository.ReadNullableString(row, "steamId"),
                    RequiredVersion: PlaySetRepository.ReadString(row, "requiredVersion"),
                    ThumbnailPath: PlaySetRepository.ReadNullableString(row, "thumbnailPath"),
                    Status: PlaySetRepository.ReadNullableString(row, "status"),
                    Picture: null) with
                {
                    ModId = string.IsNullOrEmpty(id) ? null : Guid.TryParse(id, out var parsed) ? parsed : null,
                });
            }

            return result;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("读取 mods 表失败，仅使用磁盘扫描结果：" + exception.Message);
            return new List<ModRecord>();
        }
        catch (IOException exception)
        {
            _logger.Warn("读取 mods 表失败，仅使用磁盘扫描结果：" + exception.Message);
            return new List<ModRecord>();
        }
    }

    private static int FindSqliteMatch(List<ModRecord> sqliteRows, ModRecord record)
    {
        var dirKey = PathHelper.NormalizeKey(record.DirPath);
        var registryKey = NormalizeRegistry(record.GameRegistryId);
        var steamKey = NormalizeSteam(record.SteamId);

        for (var i = 0; i < sqliteRows.Count; i++)
        {
            var candidate = sqliteRows[i];
            if (dirKey.Length > 0 && dirKey == PathHelper.NormalizeKey(candidate.DirPath))
            {
                return i;
            }

            if (registryKey.Length > 0 && registryKey == NormalizeRegistry(candidate.GameRegistryId))
            {
                return i;
            }
        }

        if (steamKey.Length > 0)
        {
            for (var i = 0; i < sqliteRows.Count; i++)
            {
                if (steamKey == NormalizeSteam(sqliteRows[i].SteamId))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private PlaySetMod Compose(GameInstallation game, ModRecord? record, ModRecord? sqlite, int position)
    {
        var name = FirstNonEmptyOrNull(
            record?.Name,
            sqlite?.Name,
            record?.DirPath is null ? null : _fileSystem.GetFileName(record.DirPath)) ?? "Unknown Mod";
        var dirPath = FirstNonEmpty(record?.DirPath, sqlite?.DirPath, string.Empty);
        var archivePath = FirstNonEmpty(record?.ArchivePath, sqlite?.ArchivePath, null);
        var registryId = FirstNonEmpty(record?.GameRegistryId, sqlite?.GameRegistryId, string.Empty);
        var requiredVersion = FirstNonEmpty(record?.RequiredVersion, sqlite?.RequiredVersion, string.Empty);
        var modId = sqlite?.ModId ?? CreateDeterministicModId(registryId, dirPath, archivePath, name);

        // .mod 描述文件里的 remote_file_id 与 metadata.json 里的 id 都是创意工坊 id，优先用扫描值；
        // mods.steamId 作为兜底（有些 mod 没有描述文件，只有数据库登记）。
        var steamId = FirstNonEmptyOrNull(record?.SteamId, sqlite?.SteamId, null);

        return new PlaySetMod(
            ModId: modId,
            Name: name,
            DirPath: dirPath,
            ArchivePath: archivePath,
            GameRegistryId: registryId,
            ThumbnailPath: ResolveThumbnail(game, record, sqlite, dirPath, archivePath, registryId, steamId, modId),
            RequiredVersion: requiredVersion,
            Status: ResolveStatus(record, sqlite, dirPath),
            // 目录扫描没有播放集上下文：Enabled 表示"该 mod 可用"，真实启用状态请用
            // IPlaySetRepository.GetPlaySetModsAsync 获取。
            Enabled: true,
            Position: position,
            SteamId: steamId);
    }

    /// <summary>
    /// 缩略图解析：交给 <see cref="ModThumbnailResolver"/>，它负责 Steam 库重定位、picture 优先与多扩展名。
    /// </summary>
    private string? ResolveThumbnail(
        GameInstallation game,
        ModRecord? record,
        ModRecord? sqlite,
        string dirPath,
        string? archivePath,
        string registryId,
        string? steamId,
        Guid modId)
        => _thumbnails.Resolve(
            game,
            dirPath: dirPath,
            archivePath: archivePath,
            picture: FirstNonEmptyOrNull(record?.Picture, sqlite?.Picture, null),
            steamId: steamId,
            gameRegistryId: registryId,
            cachedThumbnailPath: sqlite?.ThumbnailPath,
            modId: modId);

    private string ResolveStatus(ModRecord? record, ModRecord? sqlite, string dirPath)
    {
        var status = sqlite?.Status;
        if (!string.IsNullOrWhiteSpace(status))
        {
            return status;
        }

        if (!string.IsNullOrWhiteSpace(dirPath))
        {
            return _fileSystem.DirectoryExists(dirPath) ? "ready_to_play" : "missing";
        }

        return "missing";
    }

    /// <summary>
    /// 不在 sqlite 里的 mod 也需要一个稳定 id：用注册 id / 目录 / 归档 / 名字的 MD5 生成，
    /// 保证同一 mod 每次扫描得到同一个 Guid（UI 选中状态、播放集保存都依赖它稳定）。
    /// </summary>
    private static Guid CreateDeterministicModId(string registryId, string dirPath, string? archivePath, string name)
    {
        var key = NormalizeRegistry(registryId) + "|" + PathHelper.NormalizeKey(dirPath) + "|" +
                  PathHelper.NormalizeKey(archivePath) + "|" + name.ToUpperInvariant();
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string NormalizeRegistry(string? registryId)
        => string.IsNullOrWhiteSpace(registryId)
            ? string.Empty
            : registryId.Trim().Replace('\\', '/').ToUpperInvariant();

    private static string NormalizeSteam(string? steamId) => string.IsNullOrWhiteSpace(steamId) ? string.Empty : steamId.Trim();

    private static string FirstNonEmpty(string? first, string? second, string? third)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second;
        }

        return string.IsNullOrWhiteSpace(third) ? string.Empty : third;
    }

    private static string? FirstNonEmptyOrNull(string? first, string? second, string? third)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second;
        }

        return string.IsNullOrWhiteSpace(third) ? null : third;
    }

    private static string Read(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && value is not null ? value.Trim() : string.Empty;

    private static string? ReadJsonProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    /// <summary>一条 mod 记录（磁盘扫描结果或 sqlite 行），合并前的中转形态。</summary>
    private sealed record ModRecord(
        string Name,
        string? DirPath,
        string? ArchivePath,
        string GameRegistryId,
        string? SteamId,
        string? RequiredVersion,
        string? ThumbnailPath,
        string? Status,
        string? Picture)
    {
        /// <summary>sqlite 行的主键；磁盘扫描得到的记录为 null。</summary>
        public Guid? ModId { get; init; }
    }
}
