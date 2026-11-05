namespace PdxLauncher.Core.Saves;

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Games;

/// <summary>
/// 按魔数/扩展名分流的存档头解析器。
/// <para>
/// 硬约束：
/// <list type="bullet">
/// <item><description><c>SAV0</c> 类只读前 <see cref="MaxHeaderBytes"/> 字节，<c>gamestate</c> 永远不读；</description></item>
/// <item><description>zip 类只读 <c>meta</c>/<c>metadata</c> 条目，且条目解压后最多读 <see cref="MaxZipMetaBytes"/>；</description></item>
/// <item><description>任何异常都在这里吞掉并降级为「文件名 + 文件时间 + ParseState=Degraded」，绝不上抛。</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class SavContainerReader
{
    /// <summary>SAV0 类存档读取的头部字节上限。</summary>
    public const int MaxHeaderBytes = 4096;

    /// <summary>zip 元数据条目解压后的读取上限。</summary>
    public const int MaxZipMetaBytes = 64 * 1024;

    private const int ZipEntryScanLimit = 512;

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>创建解析器。</summary>
    public SavContainerReader(IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// 解析单份存档；失败时返回降级快照，不抛异常。
    /// </summary>
    /// <param name="gameId">游戏 id（会被归一）。</param>
    /// <param name="filePath">存档路径。</param>
    /// <param name="containerHint">扫描器给出的容器形态；<see cref="SaveContainer.Unknown"/> 时按扩展名嗅探。</param>
    /// <param name="fileSize">已取到的文件大小；null 时现取。</param>
    /// <param name="lastWriteUtc">已取到的最后写入时间；null 时现取。</param>
    public SaveGameMeta Parse(
        string gameId,
        string filePath,
        SaveContainer containerHint,
        long? fileSize = null,
        DateTimeOffset? lastWriteUtc = null)
    {
        ArgumentNullException.ThrowIfNull(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var canonical = KnownGames.CanonicalizeGameId(gameId);
        var fileName = Path.GetFileName(filePath);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(stem))
        {
            stem = string.IsNullOrEmpty(fileName) ? "save" : fileName;
        }

        var container = containerHint == SaveContainer.Unknown ? ResolveContainer(filePath) : containerHint;
        var size = 0L;
        var lastWrite = DateTimeOffset.MinValue;

        try
        {
            size = fileSize ?? _fileSystem.GetFileSize(filePath);
            lastWrite = lastWriteUtc ?? ReadLastWriteUtc(filePath);
            return container switch
            {
                SaveContainer.TextMeta => ParseTextMeta(canonical, filePath, fileName, stem, size, lastWrite),
                SaveContainer.BinaryToken => ParseBinaryToken(canonical, filePath, fileName, stem, size, lastWrite),
                SaveContainer.ZipMeta => ParseZipMeta(canonical, filePath, fileName, stem, size, lastWrite),
                SaveContainer.Colossal => ParseColossal(canonical, filePath, fileName, stem, size, lastWrite),
                _ => CreateFallback(canonical, filePath, fileName, stem, container, size, lastWrite, "无法识别的存档扩展名"),
            };
        }
        catch (Exception exception)
        {
            _logger.Warn("存档解析失败，降级为文件名与文件时间：" + filePath + "：" + exception.GetType().Name + "：" + exception.Message);
            return CreateFallback(
                canonical,
                filePath,
                fileName,
                stem,
                container,
                size,
                lastWrite,
                "解析异常：" + exception.GetType().Name);
        }
    }

    // ------------------------------------------------------------------
    // SAV0 + 文本 meta_data（CK3）
    // ------------------------------------------------------------------

    private SaveGameMeta ParseTextMeta(
        string gameId,
        string filePath,
        string fileName,
        string stem,
        long fileSize,
        DateTimeOffset lastWrite)
    {
        var prefix = ReadPrefix(filePath);
        if (!TryGetBodyStart(prefix, out var bodyStart, out var reason))
        {
            return CreateFallback(gameId, filePath, fileName, stem, SaveContainer.TextMeta, fileSize, lastWrite, reason);
        }

        var body = Encoding.UTF8.GetString(prefix, bodyStart, prefix.Length - bodyStart);
        var metaIndex = body.IndexOf("meta_data", StringComparison.Ordinal);
        if (metaIndex < 0)
        {
            return CreateFallback(
                gameId,
                filePath,
                fileName,
                stem,
                SaveContainer.TextMeta,
                fileSize,
                lastWrite,
                "前 4 KB 内未找到 meta_data");
        }

        if (!TryExtractBraceBlock(body, metaIndex, out var block))
        {
            return CreateFallback(
                gameId,
                filePath,
                fileName,
                stem,
                SaveContainer.TextMeta,
                fileSize,
                lastWrite,
                "meta_data 花括号未在前 4 KB 内闭合");
        }

        var values = ParseKeyValuePairs(block);
        var version = GetFirst(values, "version");
        var gameDate = GetFirst(values, "meta_date", "date");
        var playerName = GetFirst(values, "meta_player_name", "player_name");
        var metaName = GetFirst(values, "meta_title_name", "meta_save_name", "name", "title");
        var saveName = metaName ?? stem;
        var country = GetFirst(values, "meta_country", "meta_player_country", "country");
        var isIronman = IsTruthy(GetFirst(values, "ironman"));

        var hasMetadata = version is not null || gameDate is not null || metaName is not null ||
                          playerName is not null || country is not null || isIronman;
        var state = hasMetadata ? SaveParseState.Ok : SaveParseState.Degraded;
        var degradeReason = state == SaveParseState.Ok ? null : "meta_data 中没有可展示字段";

        return new SaveGameMeta(
            filePath,
            fileName,
            gameId,
            SaveContainer.TextMeta,
            saveName,
            gameDate,
            version,
            playerName,
            isIronman,
            country,
            fileSize,
            lastWrite,
            state,
            degradeReason);
    }

    // ------------------------------------------------------------------
    // SAV0 + Jomini 二进制 token 流（Victoria 3 / EU5）
    // ------------------------------------------------------------------

    private SaveGameMeta ParseBinaryToken(
        string gameId,
        string filePath,
        string fileName,
        string stem,
        long fileSize,
        DateTimeOffset lastWrite)
    {
        var prefix = ReadPrefix(filePath);
        if (!TryGetBodyStart(prefix, out var bodyStart, out var reason))
        {
            return CreateFallback(gameId, filePath, fileName, stem, SaveContainer.BinaryToken, fileSize, lastWrite, reason);
        }

        // Victoria 3 的「命名存档」在 SAV0 头之后是 zip（SAV0 + id + \n + PK\x03\x04），
        // 元数据在 meta 条目里仍是 Jomini token 流；autosave 才是裸 token 流。
        List<string> strings;
        if (IsZipHeader(prefix, bodyStart))
        {
            var metaBytes = TryReadZipEntry(filePath, IsMetadataEntry, bodyStart);
            if (metaBytes is null)
            {
                return CreateFallback(
                    gameId,
                    filePath,
                    fileName,
                    stem,
                    SaveContainer.BinaryToken,
                    fileSize,
                    lastWrite,
                    "SAV0 后的 zip 中没有 meta/metadata 条目");
            }

            strings = ReadTopLevelStrings(metaBytes, 0);
        }
        else
        {
            strings = ReadTopLevelStrings(prefix, bodyStart);
        }

        if (strings.Count == 0)
        {
            return CreateFallback(
                gameId,
                filePath,
                fileName,
                stem,
                SaveContainer.BinaryToken,
                fileSize,
                lastWrite,
                "二进制 token 流中没有可读字符串");
        }

        var version = strings.FirstOrDefault(IsVersionLike);
        string? date = null;
        string? name = null;
        string? dateRaw = null;
        foreach (var value in strings)
        {
            if (!TryParseDateWithName(value, out var parsedDate, out var parsedName))
            {
                continue;
            }

            date = parsedDate;
            name = parsedName;
            dateRaw = value;
            break;
        }

        string? playerRaw = null;
        string? playerName = null;
        foreach (var value in strings)
        {
            var marker = value.IndexOf(" #", StringComparison.Ordinal);
            if (marker > 0)
            {
                playerRaw = value;
                playerName = value[..marker].Trim();
                break;
            }
        }

        string? country = null;
        foreach (var value in strings)
        {
            if (string.Equals(value, version, StringComparison.Ordinal) ||
                string.Equals(value, dateRaw, StringComparison.Ordinal) ||
                string.Equals(value, playerRaw, StringComparison.Ordinal))
            {
                continue;
            }

            if (value.Length is > 0 and <= 64 &&
                value.IndexOfAny(['=', '{', '}', '\n', '\r']) < 0 &&
                value.Any(static character => character > 127))
            {
                country = value;
                break;
            }
        }

        var hasMetadata = version is not null || name is not null || date is not null ||
                          playerName is not null || country is not null;

        var saveName = name ?? playerName ?? stem;
        if (date is null && TrySplitFileDate(stem, out var fileBaseName, out var fileDate))
        {
            date = fileDate;
            if (name is null)
            {
                saveName = fileBaseName;
            }
        }

        var state = hasMetadata ? SaveParseState.Ok : SaveParseState.Degraded;
        var degradeReason = state == SaveParseState.Ok ? null : "二进制元数据中没有可展示字段";

        return new SaveGameMeta(
            filePath,
            fileName,
            gameId,
            SaveContainer.BinaryToken,
            saveName,
            date,
            version,
            playerName,
            false,
            country,
            fileSize,
            lastWrite,
            state,
            degradeReason);
    }

    private static List<string> ReadTopLevelStrings(byte[] data, int start)
    {
        var strings = new List<string>();
        var position = start;
        var depth = 0;
        var truncated = false;

        while (position + 2 <= data.Length)
        {
            var token = ReadUInt16(data, position);
            position += 2;

            switch (token)
            {
                case 0x0003:
                    depth++;
                    continue;
                case 0x0004:
                    depth--;
                    if (depth <= 0)
                    {
                        return strings;
                    }

                    continue;
                case 0x0001:
                    continue;
                case 0x000f:
                case 0x0017:
                case 0x0d5e:
                    if (!TryReadTokenString(data, ref position, depth == 1, strings, out var complete))
                    {
                        truncated = !complete;
                        return strings;
                    }

                    continue;
                case 0x0d42:
                    continue;
                default:
                    if (!TrySkipPayload(token, data, ref position, ref truncated))
                    {
                        // 其余 u16 都是 Id 键（无 payload）；RGB 的内部 OPEN/U32/CLOSE 留给下一轮自然走。
                    }

                    continue;
            }
        }

        return strings;
    }

    private static bool TryReadTokenString(
        byte[] data,
        ref int position,
        bool capture,
        List<string> strings,
        out bool complete)
    {
        complete = false;
        if (position + 2 > data.Length)
        {
            return false;
        }

        var length = ReadUInt16(data, position);
        position += 2;
        if (position + length > data.Length)
        {
            return false;
        }

        if (capture && length > 0)
        {
            strings.Add(Encoding.UTF8.GetString(data, position, length));
        }

        position += length;
        complete = true;
        return true;
    }

    private static bool TrySkipPayload(int token, byte[] data, ref int position, ref bool truncated)
    {
        var payload = token switch
        {
            0x000c or 0x000d or 0x0014 => 4,
            0x000e => 1,
            0x0167 or 0x029c or 0x0317 => 8,
            0x0d3e or 0x0d44 or 0x0d5b => 2,
            0x0d3f or 0x0d46 or 0x0d5d => 4,
            0x0d40 or 0x0d43 or 0x0d5a => 1,
            0x0d41 or 0x0d45 or 0x0d5c => 3,
            >= 0x0d47 and <= 0x0d55 => (token - 0x0d47) switch
            {
                var offset when offset <= 7 => offset,
                var offset => offset - 7,
            },
            _ => -1,
        };

        if (payload < 0)
        {
            return false;
        }

        if (position + payload > data.Length)
        {
            truncated = true;
            position = data.Length;
            return true;
        }

        position += payload;
        return true;
    }

    // ------------------------------------------------------------------
    // zip + meta 条目（Stellaris / CS2 .cok）
    // ------------------------------------------------------------------

    private SaveGameMeta ParseZipMeta(
        string gameId,
        string filePath,
        string fileName,
        string stem,
        long fileSize,
        DateTimeOffset lastWrite)
    {
        var metaBytes = TryReadZipEntry(
            filePath,
            static entryName => string.Equals(Path.GetFileName(entryName), "meta", StringComparison.OrdinalIgnoreCase));
        if (metaBytes is null)
        {
            return CreateFallback(
                gameId,
                filePath,
                fileName,
                stem,
                SaveContainer.ZipMeta,
                fileSize,
                lastWrite,
                "zip 中没有可读的 meta 条目");
        }

        var values = ParseKeyValuePairs(Encoding.UTF8.GetString(metaBytes));
        var version = GetFirst(values, "version");
        var gameDate = GetFirst(values, "date", "game_date");
        var metaName = GetFirst(values, "name", "save_name");
        var saveName = metaName ?? stem;
        var playerName = GetFirst(values, "player_name", "player");
        var country = GetFirst(values, "country", "player_country");
        var isIronman = IsTruthy(GetFirst(values, "ironman"));

        var hasMetadata = version is not null || gameDate is not null || metaName is not null ||
                          playerName is not null || country is not null || isIronman;
        var state = hasMetadata ? SaveParseState.Ok : SaveParseState.Degraded;
        var degradeReason = state == SaveParseState.Ok ? null : "meta 条目里没有可展示字段";

        return new SaveGameMeta(
            filePath,
            fileName,
            gameId,
            SaveContainer.ZipMeta,
            saveName,
            gameDate,
            version,
            playerName,
            isIronman,
            country,
            fileSize,
            lastWrite,
            state,
            degradeReason);
    }

    private SaveGameMeta ParseColossal(
        string gameId,
        string filePath,
        string fileName,
        string stem,
        long fileSize,
        DateTimeOffset lastWrite)
    {
        string? metaName = null;
        string? gameDate = null;
        string? version = null;
        string? playerName = null;
        string? country = null;
        var isIronman = false;
        string? degradeReason;

        if (string.Equals(Path.GetExtension(filePath), ".cok", StringComparison.OrdinalIgnoreCase))
        {
            // CS2 的 SaveGameMetadata 是 JSON；只认顶层字段，绝不去 modsEnabled 里找版本。
            var metaBytes = TryReadZipEntry(filePath, IsColossalMetadataEntry);
            if (metaBytes is null)
            {
                degradeReason = "zip 中没有 SaveGameMetadata 条目";
            }
            else if (TryParseColossalMetadata(metaBytes, out metaName, out gameDate, out version, out playerName, out country, out isIronman))
            {
                degradeReason = null;
            }
            else
            {
                degradeReason = "SaveGameMetadata 不是可解析的 JSON";
            }
        }
        else
        {
            degradeReason = "CS1 .crp 容器不含可读元数据";
        }

        var hasMetadata = metaName is not null || gameDate is not null || version is not null ||
                          playerName is not null || country is not null || isIronman;
        var state = hasMetadata ? SaveParseState.Ok : SaveParseState.Degraded;
        if (state == SaveParseState.Degraded && degradeReason is null)
        {
            degradeReason = "元数据中没有可展示字段";
        }

        return new SaveGameMeta(
            filePath,
            fileName,
            gameId,
            SaveContainer.Colossal,
            metaName ?? stem,
            gameDate,
            version,
            playerName,
            isIronman,
            country,
            fileSize,
            lastWrite,
            state,
            state == SaveParseState.Ok ? null : degradeReason);
    }

    private static bool IsMetadataEntry(string entryName)
    {
        var name = Path.GetFileName(entryName);
        return string.Equals(name, "meta", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "metadata", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsColossalMetadataEntry(string entryName)
    {
        var name = Path.GetFileName(entryName);
        return name.EndsWith(".SaveGameMetadata", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "metadata", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "meta", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseColossalMetadata(
        byte[] data,
        out string? saveName,
        out string? gameDate,
        out string? version,
        out string? playerName,
        out string? country,
        out bool isIronman)
    {
        saveName = null;
        gameDate = null;
        version = null;
        playerName = null;
        country = null;
        isIronman = false;

        var offset = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
        try
        {
            using var document = JsonDocument.Parse(data.AsMemory(offset));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            saveName = ReadJsonString(root, "cityName", "saveName", "name", "city_name", "save_name");
            version = ReadJsonString(root, "version", "gameVersion", "game_version");
            playerName = ReadJsonString(root, "playerName", "player_name");
            country = ReadJsonString(root, "country", "player_country") ?? saveName;
            isIronman = ReadJsonBoolean(root, "ironman");
            gameDate = ReadJsonString(root, "date", "gameDate", "game_date") ?? ReadSimulationDate(root);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }

    private static bool ReadJsonBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => IsTruthy(value.GetString()),
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            _ => false,
        };
    }

    private static string? ReadSimulationDate(JsonElement root)
    {
        if (!root.TryGetProperty("simulationDate", out var date) || date.ValueKind != JsonValueKind.Object ||
            !TryGetJsonInt(date, "year", out var year) || !TryGetJsonInt(date, "month", out var month))
        {
            return null;
        }

        var yearText = year.ToString(CultureInfo.InvariantCulture);
        var monthText = month.ToString("00", CultureInfo.InvariantCulture);
        return TryGetJsonInt(date, "day", out var day)
            ? yearText + "." + monthText + "." + day.ToString("00", CultureInfo.InvariantCulture)
            : yearText + "." + monthText;
    }

    private static bool TryGetJsonInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private byte[]? TryReadZipEntry(string path, Func<string, bool> predicate, long startOffset = 0)
    {
        using var stream = _fileSystem.OpenRead(path);
        using var archive = startOffset == 0
            ? new ZipArchive(stream, ZipArchiveMode.Read)
            : new ZipArchive(new SegmentStream(stream, startOffset), ZipArchiveMode.Read);
        var inspected = 0;
        foreach (var entry in archive.Entries)
        {
            inspected++;
            if (inspected > ZipEntryScanLimit)
            {
                break;
            }

            if (entry.Length > MaxZipMetaBytes || !predicate(entry.FullName))
            {
                continue;
            }

            using var entryStream = entry.Open();
            return ReadBounded(entryStream, MaxZipMetaBytes);
        }

        return null;
    }

    private static byte[] ReadBounded(Stream stream, int maxBytes)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        var remaining = maxBytes;
        while (remaining > 0)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
            remaining -= read;
        }

        return memory.ToArray();
    }

    // ------------------------------------------------------------------
    // 通用
    // ------------------------------------------------------------------

    private SaveGameMeta CreateFallback(
        string gameId,
        string filePath,
        string fileName,
        string stem,
        SaveContainer container,
        long fileSize,
        DateTimeOffset lastWrite,
        string reason)
    {
        var saveName = stem;
        string? gameDate = null;
        if (TrySplitFileDate(stem, out var baseName, out var fileDate))
        {
            saveName = baseName;
            gameDate = fileDate;
        }

        return new SaveGameMeta(
            filePath,
            fileName,
            gameId,
            container,
            saveName,
            gameDate,
            null,
            null,
            false,
            null,
            fileSize,
            lastWrite,
            SaveParseState.Degraded,
            reason);
    }

    private static bool TryGetBodyStart(byte[] data, out int bodyStart, out string reason)
    {
        bodyStart = 0;
        if (data.Length < 4 || data[0] != (byte)'S' || data[1] != (byte)'A' || data[2] != (byte)'V' || data[3] != (byte)'0')
        {
            reason = "缺少 SAV0 魔数";
            return false;
        }

        var newline = Array.IndexOf(data, (byte)'\n');
        if (newline < 0)
        {
            reason = "SAV0 头部缺少换行";
            return false;
        }

        bodyStart = newline + 1;
        reason = string.Empty;
        return true;
    }

    private static bool TryExtractBraceBlock(string body, int searchStart, out string block)
    {
        var open = body.IndexOf('{', searchStart);
        if (open < 0)
        {
            block = string.Empty;
            return false;
        }

        var depth = 0;
        var inQuotes = false;
        for (var index = open; index < body.Length; index++)
        {
            var character = body[index];
            if (inQuotes)
            {
                if (character == '\\')
                {
                    index++;
                    continue;
                }

                if (character == '"')
                {
                    inQuotes = false;
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
                continue;
            }

            if (character == '{')
            {
                depth++;
                continue;
            }

            if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    block = body[(open + 1)..index];
                    return true;
                }
            }
        }

        block = string.Empty;
        return false;
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ','))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var keyStart = index;
            while (index < text.Length &&
                   (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '-' or '.'))
            {
                index++;
            }

            if (index == keyStart)
            {
                index++;
                continue;
            }

            var key = text[keyStart..index];
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index >= text.Length || text[index] != '=')
            {
                index++;
                continue;
            }

            index++;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            string value;
            if (index < text.Length && text[index] == '"')
            {
                index++;
                var builder = new StringBuilder();
                while (index < text.Length)
                {
                    var character = text[index];
                    if (character == '\\' && index + 1 < text.Length)
                    {
                        builder.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }

                    if (character == '"')
                    {
                        index++;
                        break;
                    }

                    builder.Append(character);
                    index++;
                }

                value = builder.ToString();
            }
            else
            {
                var valueStart = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != '}')
                {
                    index++;
                }

                value = text[valueStart..index];
            }

            values[key] = value;
        }

        return values;
    }

    private static string? GetFirst(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool IsTruthy(string? value) =>
        value is not null &&
        (value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("1", StringComparison.Ordinal));

    private static bool IsVersionLike(string value)
    {
        if (value.Length is < 3 or > 24)
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) || major > 999)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                return false;
            }

            foreach (var character in part)
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryParseDateWithName(string value, out string date, out string? name)
    {
        date = string.Empty;
        name = null;

        var index = 0;
        var yearStart = 0;
        while (index < value.Length && char.IsAsciiDigit(value[index]))
        {
            index++;
        }

        var yearLength = index - yearStart;
        if (yearLength is < 3 or > 4 || index >= value.Length || value[index] != '.')
        {
            return false;
        }

        index++;
        var monthStart = index;
        while (index < value.Length && char.IsAsciiDigit(value[index]))
        {
            index++;
        }

        var monthLength = index - monthStart;
        if (monthLength is < 1 or > 2 || index >= value.Length || value[index] != '.')
        {
            return false;
        }

        index++;
        var dayStart = index;
        while (index < value.Length && char.IsAsciiDigit(value[index]))
        {
            index++;
        }

        var dayLength = index - dayStart;
        if (dayLength is < 1 or > 2)
        {
            return false;
        }

        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        if (index >= value.Length || value[index] != '-')
        {
            return false;
        }

        index++;
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        if (index >= value.Length)
        {
            return false;
        }

        name = value[index..].Trim();
        if (name.Length == 0)
        {
            return false;
        }

        date = value[..(dayStart + dayLength)];
        return true;
    }

    private static bool TrySplitFileDate(string stem, out string name, out string date)
    {
        const int suffixLength = 11;
        name = stem;
        date = string.Empty;
        if (stem.Length <= suffixLength || stem[^suffixLength] != '_')
        {
            return false;
        }

        var start = stem.Length - suffixLength;
        var year = stem.Substring(start + 1, 4);
        var month = stem.Substring(start + 6, 2);
        var day = stem.Substring(start + 9, 2);
        if (!AllAsciiDigits(year) || !AllAsciiDigits(month) || !AllAsciiDigits(day))
        {
            return false;
        }

        if (!int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var monthValue) ||
            monthValue is < 1 or > 12 ||
            !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var dayValue) ||
            dayValue is < 1 or > 31)
        {
            return false;
        }

        name = stem[..start];
        date = year + "." + month + "." + day;
        return name.Length > 0;
    }

    private static bool AllAsciiDigits(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static SaveContainer ResolveContainer(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".ck3" => SaveContainer.TextMeta,
            ".v3" or ".eu5" => SaveContainer.BinaryToken,
            ".sav" => SaveContainer.ZipMeta,
            ".cok" or ".crp" => SaveContainer.Colossal,
            _ => SaveContainer.Unknown,
        };

    private static DateTimeOffset ReadLastWriteUtc(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        }
        catch (IOException)
        {
            return DateTimeOffset.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
        catch (ArgumentException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    private byte[] ReadPrefix(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        var available = (int)Math.Min(MaxHeaderBytes, stream.Length);
        var buffer = new byte[available];
        var read = 0;
        while (read < available)
        {
            var count = stream.Read(buffer, read, available - read);
            if (count <= 0)
            {
                break;
            }

            read += count;
        }

        return read == available ? buffer : buffer[..read];
    }

    private static bool IsZipHeader(byte[] data, int offset) =>
        offset >= 0 && offset + 4 <= data.Length &&
        data[offset] == (byte)'P' && data[offset + 1] == (byte)'K' &&
        data[offset + 2] == 0x03 && data[offset + 3] == 0x04;

    /// <summary>
    /// 把「从 offset 到文件尾」的一段包装成从 0 开始的流。
    /// <para>zip 的内嵌偏移会破坏 <see cref="ZipArchive"/> 对中央目录的定位，
    /// 必须先把 SAV0 头切掉再交给它；不能整段复制，<c>gamestate</c> 可能有数百 MB。</para>
    /// </summary>
    private sealed class SegmentStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _offset;
        private readonly long _length;

        public SegmentStream(Stream inner, long offset)
        {
            _inner = inner;
            _offset = offset;
            _length = inner.Length - offset;
            _inner.Position = offset;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _inner.Position - _offset;
            set => _inner.Position = _offset + value;
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            Position = target;
            return target;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));
}
