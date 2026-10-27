namespace PdxLauncher.Core.LoadFiles;

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// <see cref="ILoadFileWriter"/> 实现：读写 Paradox 启动器的加载文件（启动必需文件）。
/// <para>文件名由 <see cref="LoadFileFormat"/> 推导：V0 → dlc_load.json，V1/V1_1 → content_load.json，
/// 一律写在 <see cref="GameInstallation.GameDataDir"/> 下。</para>
/// <para>序列化严格等价于 JS 的 JSON.stringify：紧凑、无缩进、无换行、无 BOM、中文不转义、不写 formatVersion 键。</para>
/// <para>键序优先沿用磁盘上已有文件的顶层键序（官方 defaultsDeep 行为），新键追加在后。</para>
/// </summary>
public sealed class LoadFileWriter : ILoadFileWriter
{
    /// <summary>formatVersion 0 的加载文件名。</summary>
    public const string DlcLoadFileName = "dlc_load.json";

    /// <summary>formatVersion 1 / 1.1 的加载文件名。</summary>
    public const string ContentLoadFileName = "content_load.json";

    private const string BackupSuffix = ".bak";

    private const string TempSuffix = ".tmp";

    private const string KeyDisabledDlcs = "disabled_dlcs";
    private const string KeyEnabledModsV0 = "enabled_mods";
    private const string KeyEnabledUgc = "enabledUGC";
    private const string KeyDisabledDlc = "disabledDLC";
    private const string KeyEnabledMods = "enabledMods";
    private const string KeyPath = "path";
    private const string KeyParadoxAppId = "paradoxAppId";
    private const string ArchiveExtension = ".zip";

    private static readonly string[] V0DefaultKeyOrder = { KeyDisabledDlcs, KeyEnabledModsV0 };

    private static readonly string[] V1DefaultKeyOrder = { KeyEnabledUgc, KeyDisabledDlc, KeyEnabledMods };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>全局写入闸门：同一时刻只允许一个加载文件写入（官方启动器也是串行的）。</summary>
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>创建加载文件读写器。</summary>
    public LoadFileWriter(IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>格式对应的加载文件名。</summary>
    public static string GetFileName(LoadFileFormat format) => format switch
    {
        LoadFileFormat.DlcLoadV0 => DlcLoadFileName,
        LoadFileFormat.ContentLoadV1 => ContentLoadFileName,
        LoadFileFormat.ContentLoadV1_1 => ContentLoadFileName,
        _ => ContentLoadFileName,
    };

    /// <summary>游戏的加载文件完整路径。</summary>
    public string GetFilePath(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return _fileSystem.Combine(game.GameDataDir, GetFileName(game.Format));
    }

    // ------------------------------------------------------------ 序列化

    /// <inheritdoc />
    public string Serialize(GameInstallation game, LoadFileContent content)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(content);

        var keyOrder = ResolveKeyOrder(game);
        using var buffer = new MemoryStream(256);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var key in keyOrder)
            {
                WriteSection(writer, game.Format, key, content);
            }

            writer.WriteEndObject();
        }

        return Utf8NoBom.GetString(buffer.ToArray());
    }

    /// <summary>
    /// 决定顶层键序：磁盘上已有文件时沿用其键序（只保留本格式认识的键），
    /// 其余键按默认顺序追加；文件不存在/损坏时用默认键序。
    /// </summary>
    private IReadOnlyList<string> ResolveKeyOrder(GameInstallation game)
    {
        var defaults = game.Format == LoadFileFormat.DlcLoadV0 ? V0DefaultKeyOrder : V1DefaultKeyOrder;
        var existing = TryReadExistingKeyOrder(GetFilePath(game));
        if (existing is null || existing.Count == 0)
        {
            return defaults;
        }

        var order = new List<string>(defaults.Length);
        foreach (var key in existing)
        {
            if (Array.IndexOf(defaults, key) >= 0 && !order.Contains(key, StringComparer.Ordinal))
            {
                order.Add(key);
            }
        }

        foreach (var key in defaults)
        {
            if (!order.Contains(key, StringComparer.Ordinal))
            {
                order.Add(key);
            }
        }

        return order;
    }

    private IReadOnlyList<string>? TryReadExistingKeyOrder(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !_fileSystem.FileExists(path))
            {
                return null;
            }

            var text = _fileSystem.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var keys = new List<string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                keys.Add(property.Name);
            }

            return keys;
        }
        catch (JsonException exception)
        {
            _logger.Warn("现有加载文件解析失败，改用默认键序：" + path + "：" + exception.Message);
            return null;
        }
        catch (IOException exception)
        {
            _logger.Warn("现有加载文件读取失败，改用默认键序：" + path + "：" + exception.Message);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("现有加载文件无读取权限，改用默认键序：" + path + "：" + exception.Message);
            return null;
        }
        catch (ArgumentException exception)
        {
            _logger.Warn("加载文件路径非法，改用默认键序：" + path + "：" + exception.Message);
            return null;
        }
    }

    private static void WriteSection(
        Utf8JsonWriter writer,
        LoadFileFormat format,
        string key,
        LoadFileContent content)
    {
        switch (key)
        {
            case KeyDisabledDlcs:
                WriteStringArray(writer, key, content.DisabledDlcFilePaths);
                break;

            case KeyEnabledModsV0:
                WriteStringArray(writer, key, content.EnabledModRegistryIds);
                break;

            case KeyEnabledUgc:
                WritePathObjectArray(writer, key, content.EnabledUgcPaths);
                break;

            case KeyDisabledDlc:
                WriteParadoxAppIdArray(writer, key, content.DisabledDlcPopsIds);
                break;

            case KeyEnabledMods:
                writer.WritePropertyName(key);
                writer.WriteStartArray();
                if (format == LoadFileFormat.ContentLoadV1_1)
                {
                    WritePathObjects(writer, content.EnabledModDirPaths);
                }

                WritePathObjects(writer, content.EnabledModArchivePaths);
                writer.WriteEndArray();
                break;

            default:
                // 不认识的键（如 formatVersion）一律不写。
                break;
        }
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string key, IReadOnlyList<string> values)
    {
        writer.WritePropertyName(key);
        writer.WriteStartArray();
        if (values is not null)
        {
            foreach (var value in values)
            {
                if (value is not null)
                {
                    writer.WriteStringValue(value);
                }
            }
        }

        writer.WriteEndArray();
    }

    private static void WritePathObjectArray(Utf8JsonWriter writer, string key, IReadOnlyList<string> values)
    {
        writer.WritePropertyName(key);
        writer.WriteStartArray();
        WritePathObjects(writer, values);
        writer.WriteEndArray();
    }

    private static void WritePathObjects(Utf8JsonWriter writer, IReadOnlyList<string> values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            writer.WriteStartObject();
            writer.WriteString(KeyPath, value);
            writer.WriteEndObject();
        }
    }

    private static void WriteParadoxAppIdArray(Utf8JsonWriter writer, string key, IReadOnlyList<string> values)
    {
        writer.WritePropertyName(key);
        writer.WriteStartArray();
        if (values is not null)
        {
            foreach (var value in values)
            {
                if (value is null)
                {
                    continue;
                }

                writer.WriteStartObject();
                writer.WriteString(KeyParadoxAppId, value);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
    }

    // ------------------------------------------------------------ 读取

    /// <inheritdoc />
    public Task<LoadFileContent> ReadAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetFilePath(game);
        if (!_fileSystem.FileExists(path))
        {
            _logger.Info("加载文件不存在，按空内容处理：" + path);
            return Task.FromResult(LoadFileContent.Empty);
        }

        try
        {
            var text = _fileSystem.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.Warn("加载文件为空，按空内容处理：" + path);
                return Task.FromResult(LoadFileContent.Empty);
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.Warn("加载文件根节点不是对象，按空内容处理：" + path);
                return Task.FromResult(LoadFileContent.Empty);
            }

            return Task.FromResult(ParseContent(game.Format, root));
        }
        catch (JsonException exception)
        {
            _logger.Warn("加载文件解析失败，按空内容处理：" + path + "：" + exception.Message);
            return Task.FromResult(LoadFileContent.Empty);
        }
        catch (IOException exception)
        {
            _logger.Warn("加载文件读取失败，按空内容处理：" + path + "：" + exception.Message);
            return Task.FromResult(LoadFileContent.Empty);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("加载文件无读取权限，按空内容处理：" + path + "：" + exception.Message);
            return Task.FromResult(LoadFileContent.Empty);
        }
        catch (ArgumentException exception)
        {
            _logger.Warn("加载文件路径非法，按空内容处理：" + path + "：" + exception.Message);
            return Task.FromResult(LoadFileContent.Empty);
        }
    }

    private static LoadFileContent ParseContent(LoadFileFormat format, JsonElement root)
    {
        var disabledDlcs = ReadStringArray(root, KeyDisabledDlcs);
        var enabledModsV0 = ReadStringArray(root, KeyEnabledModsV0);
        var ugcPaths = ReadObjectPathArray(root, KeyEnabledUgc, KeyPath);
        var disabledPops = ReadObjectPathArray(root, KeyDisabledDlc, KeyParadoxAppId);
        var enabledModPaths = ReadObjectPathArray(root, KeyEnabledMods, KeyPath);

        var dirPaths = new List<string>();
        var archivePaths = new List<string>();
        foreach (var path in enabledModPaths)
        {
            if (IsArchivePath(path))
            {
                archivePaths.Add(path);
            }
            else
            {
                dirPaths.Add(path);
            }
        }

        if (format == LoadFileFormat.DlcLoadV0)
        {
            return new LoadFileContent(
                disabledDlcs,
                Array.Empty<string>(),
                enabledModsV0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        return new LoadFileContent(
            Array.Empty<string>(),
            disabledPops,
            Array.Empty<string>(),
            dirPaths,
            archivePaths,
            ugcPaths);
    }

    /// <summary>
    /// 加载文件不区分"目录 mod"与"压缩包 mod"（enabledMods 只有 path），
    /// 只能按扩展名判断：.zip 视为 archive，其余视为目录。V3 的 modExtension 是 zip，规则成立。
    /// </summary>
    private static bool IsArchivePath(string path) =>
        path.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ReadObjectPathArray(JsonElement root, string key, string propertyName)
    {
        if (!root.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = property.GetString();
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    // ------------------------------------------------------------ 写入

    /// <inheritdoc />
    public async Task WriteAsync(
        GameInstallation game,
        LoadFileContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetFilePath(game);
        var json = Serialize(game, content);
        var bytes = Utf8NoBom.GetBytes(json);
        var tempPath = path + TempSuffix;

        await WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.EnsureDirectory(game.GameDataDir);

            // 1) 先写临时文件，失败时磁盘上的正式文件保持原样。
            _fileSystem.WriteAllBytesAtomic(tempPath, bytes);

            // 2) 备份旧文件（覆盖上一份 .bak），备份失败不阻塞写入。
            if (_fileSystem.FileExists(path))
            {
                try
                {
                    _fileSystem.Copy(path, path + BackupSuffix, overwrite: true);
                }
                catch (IOException exception)
                {
                    _logger.Warn("加载文件备份失败，继续写入新内容：" + path + "：" + exception.Message);
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.Warn("加载文件备份无权限，继续写入新内容：" + path + "：" + exception.Message);
                }
            }

            // 3) 原子替换：File.Move(overwrite: true) 在同一卷上是原子的。
            File.Move(tempPath, path, overwrite: true);
            _logger.Info("已写入加载文件：" + path + "（" + bytes.Length + " B）");
        }
        catch (IOException exception)
        {
            _logger.Error("加载文件写入失败：" + path, exception);
            TryDeleteTemp(tempPath);
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Error("加载文件写入无权限：" + path, exception);
            TryDeleteTemp(tempPath);
            throw;
        }
        finally
        {
            WriteGate.Release();
        }
    }

    private void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (_fileSystem.FileExists(tempPath))
            {
                _fileSystem.Delete(tempPath);
            }
        }
        catch (IOException exception)
        {
            _logger.Warn("临时加载文件清理失败：" + tempPath + "：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("临时加载文件清理无权限：" + tempPath + "：" + exception.Message);
        }
    }
}
