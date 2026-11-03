namespace PdxLauncher.Core.Updates;

using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Online;

/// <summary>
/// GitHub Releases 上的更新清单（<c>latest/download/update.json</c>）。
/// 只接受 HTTPS + 白名单主机，拒绝降级、路径穿越与不合法摘要。
/// </summary>
public sealed class UpdateManifest
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>清单 schema 版本；目前只支持 1。</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    /// <summary>发布版本，如 <c>1.2.0</c>。</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>安装包下载地址（HTTPS、白名单主机）。</summary>
    [JsonPropertyName("setupUrl")]
    public string SetupUrl { get; init; } = string.Empty;

    /// <summary>安装包 SHA-256（64 位十六进制）。</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>安装包字节数。</summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }

    /// <summary>可选的发布说明网页。</summary>
    [JsonPropertyName("releaseNotesUrl")]
    public string? ReleaseNotesUrl { get; init; }

    /// <summary>可选的短发布说明（直接显示在更新浮层里）。</summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; init; }

    /// <summary>是否强制更新（当前版本低于最低要求时也应置 true）。</summary>
    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; init; }

    /// <summary>发布时间（UTC）。</summary>
    [JsonPropertyName("publishedUtc")]
    public DateTimeOffset? PublishedUtc { get; init; }

    public Uri SetupUri => new(SetupUrl, UriKind.Absolute);

    public static bool TryParse(string? json, out UpdateManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "更新清单为空";
            return false;
        }

        UpdateManifest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<UpdateManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            error = "更新清单解析失败: " + ex.Message;
            return false;
        }

        if (parsed is null)
        {
            error = "更新清单是 null";
            return false;
        }

        if (parsed.SchemaVersion != 1)
        {
            error = "不支持的更新清单 schemaVersion=" + parsed.SchemaVersion;
            return false;
        }

        if (!UpdateVersion.TryParse(parsed.Version, out _))
        {
            error = "更新清单 version 无效: " + parsed.Version;
            return false;
        }

        if (!Uri.TryCreate(parsed.SetupUrl, UriKind.Absolute, out var setupUri) ||
            !string.Equals(setupUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !OnlineHosts.IsAllowed(setupUri))
        {
            error = "更新清单 setupUrl 必须是白名单 HTTPS 地址";
            return false;
        }

        if (setupUri.AbsolutePath.EndsWith('/') ||
            !setupUri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            error = "更新清单 setupUrl 必须指向 .exe 文件";
            return false;
        }

        var sha = parsed.Sha256.Trim();
        if (sha.Length != 64 || !sha.All(IsHex))
        {
            error = "更新清单 sha256 必须是 64 位十六进制";
            return false;
        }

        if (parsed.Size <= 0)
        {
            error = "更新清单 size 必须大于 0";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(parsed.ReleaseNotesUrl) &&
            (!Uri.TryCreate(parsed.ReleaseNotesUrl, UriKind.Absolute, out var notesUri) ||
             !string.Equals(notesUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            error = "更新清单 releaseNotesUrl 必须是 HTTPS 地址";
            return false;
        }

        manifest = new UpdateManifest
        {
            SchemaVersion = parsed.SchemaVersion,
            Version = UpdateVersion.Parse(parsed.Version).ToString(),
            SetupUrl = setupUri.AbsoluteUri,
            Sha256 = sha.ToLowerInvariant(),
            Size = parsed.Size,
            ReleaseNotesUrl = parsed.ReleaseNotesUrl,
            ReleaseNotes = parsed.ReleaseNotes,
            Mandatory = parsed.Mandatory,
            PublishedUtc = parsed.PublishedUtc,
        };
        return true;

        static bool IsHex(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
