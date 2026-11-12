namespace PdxLauncher.Core.ModStudio;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Games;

/// <summary>
/// 上传草稿的文件存储：<c>&lt;data&gt;\mcp\upload-drafts\&lt;gameId&gt;\&lt;路径哈希&gt;.json</c>。
/// <para>
/// 用文件而不是 SQLite：独立 MCP 进程不打开启动器的数据库，避免和运行中的启动器
/// 争用连接；写入沿用 <see cref="IFileSystem.WriteAllTextAtomic"/> 的临时文件 + rename，
/// 启动器读到的永远是完整 JSON。
/// </para>
/// </summary>
public sealed class ModUploadDraftStore
{
    private const string McpFolderName = "mcp";
    private const string DraftsFolderName = "upload-drafts";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IFileSystem _fileSystem;
    private readonly string _root;

    /// <summary>创建存储。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="dataRoot">启动器数据根（通常是 <c>AppServices.DataRoot</c>）。</param>
    public ModUploadDraftStore(IFileSystem fileSystem, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        _fileSystem = fileSystem;
        _root = fileSystem.Combine(
            fileSystem.GetFullPath(dataRoot),
            McpFolderName,
            DraftsFolderName);
    }

    /// <summary>草稿根目录（诊断与文档用）。</summary>
    public string RootDirectory => _root;

    /// <summary>取一条草稿的文件路径。</summary>
    public string PathFor(string gameId, string modPath)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        var normalized = NormalizePath(modPath);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToLowerInvariant()));
        return _fileSystem.Combine(
            _root,
            SafeGameFolder(canonical),
            Convert.ToHexString(hash)[..16].ToLowerInvariant() + ".json");
    }

    /// <summary>读一条草稿；没有、JSON 坏掉或版本不认识时返回 null。</summary>
    public ModUploadDraft? TryGet(string gameId, string modPath)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        var normalized = NormalizePath(modPath);
        var path = PathFor(canonical, normalized);
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = _fileSystem.ReadAllText(path);
            var draft = JsonSerializer.Deserialize<ModUploadDraft>(json, JsonOptions);
            if (draft is null || draft.SchemaVersion != ModUploadDraft.CurrentSchemaVersion)
            {
                return null;
            }

            // 文件名只是哈希，仍要核对内容，避免把别的 mod 的草稿填进来。
            return string.Equals(
                       KnownGames.CanonicalizeGameId(draft.GameId),
                       canonical,
                       StringComparison.OrdinalIgnoreCase)
                   && string.Equals(
                       NormalizePath(draft.ModPath),
                       normalized,
                       StringComparison.OrdinalIgnoreCase)
                ? draft
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>写一条草稿；成功返回 true。</summary>
    public bool Save(ModUploadDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var stored = draft with
        {
            GameId = KnownGames.CanonicalizeGameId(draft.GameId),
            ModPath = NormalizePath(draft.ModPath),
            Tags = draft.Tags ?? Array.Empty<string>(),
            SchemaVersion = ModUploadDraft.CurrentSchemaVersion,
            UpdatedAt = draft.UpdatedAt == default ? DateTimeOffset.UtcNow : draft.UpdatedAt,
        };

        try
        {
            var json = JsonSerializer.Serialize(stored, JsonOptions);
            _fileSystem.WriteAllTextAtomic(PathFor(stored.GameId, stored.ModPath), json);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>删除一条草稿；返回是否真的删了文件。</summary>
    public bool Delete(string gameId, string modPath)
    {
        var path = PathFor(gameId, modPath);
        if (!_fileSystem.FileExists(path))
        {
            return false;
        }

        try
        {
            _fileSystem.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return _fileSystem.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string SafeGameFolder(string gameId)
    {
        var builder = new StringBuilder(gameId.Length);
        foreach (var ch in gameId)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
