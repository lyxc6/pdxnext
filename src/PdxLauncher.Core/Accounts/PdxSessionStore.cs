namespace PdxLauncher.Core.Accounts;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// Paradox 会话的落盘存储，文件为 <c>data\pdx-session.json</c>。
/// <para>
/// 会话 token 与续期 token 都经 <see cref="ISecretProtector"/> 保护后再写盘
/// （Windows 上是与当前用户绑定的 DPAPI）；邮箱与显示名是明文，因为它们只用于界面显示。
/// 邮箱不参与任何网络请求 —— 登录时用户会重新输入。
/// </para>
/// <para>
/// 读取容错：文件缺失、损坏、解不开（换了 Windows 用户或换了机器）一律视为「没有会话」，
/// 记一条 Warn 并返回 null，绝不抛异常。
/// </para>
/// </summary>
public sealed class PdxSessionStore
{
    /// <summary>文件名（放在数据根下）。</summary>
    public const string FileName = "pdx-session.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly ISecretProtector _protector;

    /// <summary>创建存储。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="logger">日志。</param>
    /// <param name="protector">敏感字段保护器。</param>
    /// <param name="path">文件路径；null 用 <c>data\pdx-session.json</c>（自检可指向临时目录）。</param>
    public PdxSessionStore(
        IFileSystem fileSystem,
        IAppLogger logger,
        ISecretProtector protector,
        string? path = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(protector);

        _fileSystem = fileSystem;
        _logger = logger;
        _protector = protector;
        Path = path ?? DefaultPath;
    }

    /// <summary>会话文件的绝对路径。</summary>
    public string Path { get; }

    /// <summary>默认位置：数据根下的 <c>pdx-session.json</c>。</summary>
    public static string DefaultPath => System.IO.Path.Combine(PdxDataRoot.Directory, FileName);

    /// <summary>读回上次保存的会话；没有或解不开返回 null。</summary>
    public PdxSession? Load()
    {
        if (!_fileSystem.FileExists(Path))
        {
            return null;
        }

        try
        {
            var json = _fileSystem.ReadAllText(Path);
            var node = JsonNode.Parse(json) as JsonObject;
            if (node is null)
            {
                _logger.Warn("pdx-session.json 不是对象，忽略。");
                return null;
            }

            var token = UnprotectField(node, "sessionToken");
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var refresh = UnprotectField(node, "refreshToken");
            var userId = node["userId"]?.GetValue<string>() ?? string.Empty;
            var email = node["email"]?.GetValue<string>() ?? string.Empty;
            var displayName = node["displayName"]?.GetValue<string>() ?? string.Empty;

            // 时间戳坏了不该让整份会话作废：解析失败时按「刚拿到」处理，下一次续期再校正。
            var obtainedAt = node["obtainedAt"] is JsonValue obtained
                && obtained.TryGetValue<string>(out var text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                    ? parsed
                    : DateTimeOffset.UtcNow;

            return new PdxSession(token, refresh, userId, email, displayName, obtainedAt);
        }
        catch (JsonException ex)
        {
            _logger.Warn("pdx-session.json 解析失败，视为未登录: " + ex.Message);
            return null;
        }
        catch (IOException ex)
        {
            _logger.Warn("pdx-session.json 读取失败，视为未登录: " + ex.Message);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("pdx-session.json 无权限读取，视为未登录: " + ex.Message);
            return null;
        }
    }

    /// <summary>保存会话（原子写）。失败只记日志，不向 UI 抛。</summary>
    public void Save(PdxSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            var node = new JsonObject
            {
                ["scheme"] = _protector.Scheme,
                ["sessionToken"] = _protector.Protect(session.SessionToken),
                ["refreshToken"] = session.RefreshToken is null ? null : _protector.Protect(session.RefreshToken),
                ["userId"] = session.UserId,
                ["email"] = session.Email,
                ["displayName"] = session.DisplayName,
                ["obtainedAt"] = session.ObtainedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            };

            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.EnsureDirectory(directory);
            }

            _fileSystem.WriteAllTextAtomic(Path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException ex)
        {
            _logger.Error("pdx-session.json 写入失败", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error("pdx-session.json 写入失败（无权限）", ex);
        }
    }

    /// <summary>删除会话文件（登出）。文件本来就不存在也算成功。</summary>
    public void Clear()
    {
        try
        {
            if (_fileSystem.FileExists(Path))
            {
                _fileSystem.Delete(Path);
            }
        }
        catch (IOException ex)
        {
            _logger.Warn("pdx-session.json 删除失败: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("pdx-session.json 删除失败（无权限）: " + ex.Message);
        }
    }

    private string? UnprotectField(JsonObject node, string name)
    {
        var protectedText = node[name]?.GetValue<string>();
        return string.IsNullOrEmpty(protectedText) ? null : _protector.Unprotect(protectedText);
    }
}
