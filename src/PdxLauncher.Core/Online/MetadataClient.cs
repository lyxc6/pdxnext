namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// Paradox 元数据 GraphQL 客户端（匿名 POST，实测 HTTP 200）。
/// 容错原则：非 200、JSON 畸形、字段缺失一律返回空列表并记日志，绝不抛给上层。
/// </summary>
public sealed class MetadataClient : IMetadataClient
{
    /// <summary>与官方启动器一致的 GetMetadata 查询（字段裁剪到本产品用到的部分）。</summary>
    public const string Query = """
        query GetMetadata($gameIds: [ID]) {
          games(ids: $gameIds) {
            id
            name
            themeSettings {
              appIcon
              appTaskbarIcon
              background
              logo
              button { backgroundImage color fontName fontPath fontSize fontWeight textShadow }
              buttonHover { backgroundImage color fontName fontPath fontSize fontWeight textShadow }
              buttonDisabled { backgroundImage color fontName fontPath fontSize fontWeight textShadow }
              buttonText { backgroundImage color fontName fontPath fontSize fontWeight textShadow }
            }
            alternativeExecutables { label { en zhhans } exeArgs exePath }
            browserDlcUrl
            browserModUrl
            dlcPath
            exeArgs
            exePath
            formatVersion
          }
        }
        """;

    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IHttpFetcher _http;
    private readonly IAppLogger _logger;

    public MetadataClient(IHttpFetcher http, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GameMetadataDto>> GetMetadataAsync(
        IReadOnlyList<string> gameIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gameIds);

        var ids = new List<string>(gameIds.Count);
        foreach (var id in gameIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id.Trim());
            }
        }

        if (ids.Count == 0)
        {
            return Array.Empty<GameMetadataDto>();
        }

        var body = BuildRequestBody(ids);
        string responseText;
        try
        {
            responseText = await _http
                .PostJsonAsync(OnlineHosts.MetadataEndpoint, body, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // 非调用方取消（多为 HttpClient.Timeout 180s 触发）按失败降级处理。
            _logger.Warn("元数据接口超时，返回空列表: " + ex.Message);
            return Array.Empty<GameMetadataDto>();
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn("元数据接口请求失败，返回空列表: " + ex.Message);
            return Array.Empty<GameMetadataDto>();
        }
        catch (IOException ex)
        {
            _logger.Warn("元数据接口读取失败，返回空列表: " + ex.Message);
            return Array.Empty<GameMetadataDto>();
        }

        return Parse(responseText);
    }

    private static string BuildRequestBody(IReadOnlyList<string> ids)
    {
        var variables = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["gameIds"] = ids,
        };

        var request = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["operationName"] = "GetMetadata",
            ["query"] = Query,
            ["variables"] = variables,
        };

        return JsonSerializer.Serialize(request, RequestOptions);
    }

    private IReadOnlyList<GameMetadataDto> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.Warn("元数据响应为空，返回空列表");
            return Array.Empty<GameMetadataDto>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("games", out var games)
                || games.ValueKind != JsonValueKind.Array)
            {
                _logger.Warn("元数据响应缺少 data.games，返回空列表");
                return Array.Empty<GameMetadataDto>();
            }

            var result = new List<GameMetadataDto>(games.GetArrayLength());
            foreach (var node in games.EnumerateArray())
            {
                var dto = ParseGame(node);
                if (dto is not null)
                {
                    result.Add(dto);
                }
            }

            _logger.Info("元数据解析成功: " + result.Count + " 条");
            return result;
        }
        catch (JsonException ex)
        {
            _logger.Warn("元数据 JSON 解析失败，返回空列表: " + ex.Message);
            return Array.Empty<GameMetadataDto>();
        }
    }

    private static GameMetadataDto? ParseGame(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetString(node, "id");
        if (id is null)
        {
            return null;
        }

        return new GameMetadataDto(
            id,
            GetString(node, "name"),
            GetString(node, "exePath"),
            GetStringArray(node, "exeArgs"),
            GetString(node, "dlcPath"),
            GetString(node, "browserDlcUrl"),
            GetString(node, "browserModUrl"),
            GetString(node, "formatVersion"),
            ParseThemeSettings(node));
    }

    private static ThemeSettingsDto? ParseThemeSettings(JsonElement gameNode)
    {
        if (!gameNode.TryGetProperty("themeSettings", out var theme) || theme.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var button = GetObject(theme, "button");
        var hover = GetObject(theme, "buttonHover");
        return new ThemeSettingsDto(
            GetString(theme, "appIcon"),
            GetString(theme, "appTaskbarIcon"),
            GetString(theme, "background"),
            GetString(theme, "logo"),
            GetString(button, "backgroundImage"),
            GetString(button, "fontPath"),
            GetString(button, "color"),
            GetString(hover, "color"),
            GetDouble(button, "fontSize"),
            GetInt(button, "fontWeight"),
            GetString(button, "textShadow"));
    }

    private static string? GetString(JsonElement? parent, string name)
    {
        if (parent is not { } element)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static JsonElement? GetObject(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Object ? value : null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = item.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                list.Add(text);
            }
        }

        return list;
    }

    private static double? GetDouble(JsonElement? parent, string name)
    {
        if (parent is not { } element
            || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? GetInt(JsonElement? parent, string name)
    {
        if (parent is not { } element
            || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
