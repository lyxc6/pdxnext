namespace PdxLauncher.Core.Accounts;

using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// Paradox 账号客户端（纯 HTTP，不依赖闭源 <c>PDX.SDK.dll</c>）。
/// <para>
/// 协议全部来自官方 Electron 启动器的登录实现（本机逆向 app.asar 逐字核对）：
/// </para>
/// <list type="bullet">
/// <item>登录：<c>PUT /accounts/sessions/launcher-v2</c>，
/// 头 <c>Authorization: {"hawk":{"email":"…","sha256(password+salt)":"…"}}</c>。</item>
/// <item>续期：同一个端点，头 <c>{"renewal":{"token":"&lt;refresh_token&gt;"}}</c>。</item>
/// <item>其它请求：头 <c>{"session":{"token":"&lt;session token&gt;"}}</c> —— <b>不是 Bearer</b>。</item>
/// <item>账号资料：<c>GET /accountdetails</c>。</item>
/// </list>
/// <para>
/// 所有失败都映射成 <see cref="AppResult{T}"/> + <see cref="ErrorKind"/>，
/// 错误串只放错误码（<c>pdx-login-failed:401</c>），中文文案由界面层按码翻译。
/// </para>
/// </summary>
public sealed class PdxAccountClient
{
    /// <summary>登录会话端点（<c>launcher-v2</c> 是官方启动器使用的 client 名）。</summary>
    public static Uri SessionEndpoint { get; } = new("https://api.paradox-interactive.com/accounts/sessions/launcher-v2");

    /// <summary>账号资料端点。</summary>
    public static Uri AccountDetailsEndpoint { get; } = new("https://api.paradox-interactive.com/accountdetails");

    private readonly IPdxHttpTransport _http;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;

    /// <summary>创建客户端。</summary>
    public PdxAccountClient(IPdxHttpTransport http, IAppLogger logger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        _http = http;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>
    /// 用邮箱 + 密码登录。密码只在本方法栈上停留，不写日志、不进字段。
    /// </summary>
    /// <param name="email">Paradox 账号邮箱。</param>
    /// <param name="password">明文密码（内部会按官方算法哈希）。</param>
    /// <param name="rememberMe">是否请求 refresh token（会影响续期能力）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<PdxSession>> SignInAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return AppResult<PdxSession>.Fail(ErrorKind.Invalid, "pdx-email-empty");
        }

        if (string.IsNullOrEmpty(password))
        {
            return AppResult<PdxSession>.Fail(ErrorKind.Invalid, "pdx-password-empty");
        }

        var header = new JsonObject
        {
            ["hawk"] = new JsonObject
            {
                ["email"] = email.Trim(),
                [PdxPassword.HeaderKeyName] = PdxPassword.Hash(password),
                ["rememberMe"] = rememberMe,
            },
        };

        PdxHttpResponse response;
        try
        {
            response = await _http
                .SendAsync("PUT", SessionEndpoint, header.ToJsonString(), null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("Paradox 登录请求失败（网络）: " + ex.Message);
            return AppResult<PdxSession>.Fail(ErrorKind.Io, "pdx-login-network");
        }

        if (response.StatusCode is 401 or 403)
        {
            // 不区分「邮箱不存在」与「密码错误」，避免账号枚举；日志里留状态码便于排障。
            _logger.Warn("Paradox 登录被拒：HTTP " + response.StatusCode.ToString(CultureInfo.InvariantCulture) + " " + BodySnippet(response.Body));
            return AppResult<PdxSession>.Fail(ErrorKind.Unauthorized, "pdx-login-unauthorized");
        }

        if (!response.IsSuccess)
        {
            // 服务端的 errorCode 是唯一能区分「参数被拒 / 账号被锁 / 服务故障」的线索，
            // 不记日志的话界面上只剩一个 pdx-login-failed:xxx，等于没法排障。
            _logger.Warn("Paradox 登录失败：HTTP " + response.StatusCode.ToString(CultureInfo.InvariantCulture) + " " + BodySnippet(response.Body));
            return AppResult<PdxSession>.Fail(ErrorKind.Io, "pdx-login-failed:" + response.StatusCode.ToString(CultureInfo.InvariantCulture));
        }

        return ParseSession(response.Body, email.Trim());
    }

    /// <summary>用 refresh token 换一份新的会话。</summary>
    public async Task<AppResult<PdxSession>> RefreshAsync(PdxSession current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!current.CanRefresh)
        {
            return AppResult<PdxSession>.Fail(ErrorKind.Unauthorized, "pdx-refresh-missing");
        }

        var header = new JsonObject
        {
            ["renewal"] = new JsonObject { ["token"] = current.RefreshToken },
        };

        PdxHttpResponse response;
        try
        {
            response = await _http
                .SendAsync("PUT", SessionEndpoint, header.ToJsonString(), null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("Paradox 续期请求失败（网络）: " + ex.Message);
            return AppResult<PdxSession>.Fail(ErrorKind.Io, "pdx-refresh-network");
        }

        if (response.StatusCode is 401 or 403)
        {
            _logger.Warn("Paradox 续期被拒：HTTP " + response.StatusCode.ToString(CultureInfo.InvariantCulture) + " " + BodySnippet(response.Body));
            return AppResult<PdxSession>.Fail(ErrorKind.Unauthorized, "pdx-refresh-unauthorized");
        }

        if (!response.IsSuccess)
        {
            _logger.Warn("Paradox 续期失败：HTTP " + response.StatusCode.ToString(CultureInfo.InvariantCulture) + " " + BodySnippet(response.Body));
            return AppResult<PdxSession>.Fail(ErrorKind.Io, "pdx-refresh-failed:" + response.StatusCode.ToString(CultureInfo.InvariantCulture));
        }

        return ParseSession(response.Body, current.Email);
    }

    /// <summary>拉账号资料（显示名等）；失败不阻断主流程。</summary>
    public async Task<AppResult<string>> GetDisplayNameAsync(PdxSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            var response = await _http
                .SendAsync("GET", AccountDetailsEndpoint, SessionHeader(session), null, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return AppResult<string>.Fail(ErrorKind.Io, "pdx-account-failed:" + response.StatusCode.ToString(CultureInfo.InvariantCulture));
            }

            var node = JsonNode.Parse(response.Body) as JsonObject;
            var name = FindString(node, "displayName", "display_name", "username", "name");
            return name is null
                ? AppResult<string>.Fail(ErrorKind.NotFound, "pdx-account-no-name")
                : AppResult<string>.Ok(name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("Paradox 账号资料请求失败: " + ex.Message);
            return AppResult<string>.Fail(ErrorKind.Io, "pdx-account-network");
        }
    }

    /// <summary>构造后续请求用的 <c>Authorization</c> 头值。</summary>
    public static string SessionHeader(PdxSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new JsonObject
        {
            ["session"] = new JsonObject { ["token"] = session.SessionToken },
        }.ToJsonString();
    }

    private AppResult<PdxSession> ParseSession(string body, string email)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(body) as JsonObject;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.Warn("Paradox 登录响应不是 JSON: " + ex.Message);
            return AppResult<PdxSession>.Fail(ErrorKind.Io, "pdx-login-bad-json");
        }

        // 服务端把会话放在 session 下；个别返回是扁平的，两种都认。
        var session = root?["session"] as JsonObject ?? root;
        var token = session?["token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token))
        {
            _logger.Warn("Paradox 登录响应缺少 token。");
            return AppResult<PdxSession>.Fail(ErrorKind.Io, "pdx-login-no-token");
        }

        var userId = session?["userid"]?.GetValue<string>() ?? string.Empty;
        var refresh = session?["refresh_token"]?.GetValue<string>();
        var displayName = FindString(root, "displayName", "display_name", "username") ?? email;
        return AppResult<PdxSession>.Ok(
            new PdxSession(token, refresh, userId, email, displayName, _clock.UtcNow));
    }

    /// <summary>
    /// 取响应正文的一小段写进日志。<b>只用于响应</b>：请求侧的头里含密码哈希与会话令牌，绝不落日志。
    /// 服务端的 <c>errorCode</c>（如 <c>not-authorized</c>、<c>invalid-credentials</c>）是区分
    /// 「密码错」「账号被锁」「服务端故障」的唯一线索，没有它，界面上只剩一个 <c>pdx-login-failed:500</c>。
    /// </summary>
    private static string BodySnippet(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "<empty>";
        }

        var flattened = body.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return flattened.Length <= 200 ? flattened : flattened[..200] + "…";
    }

    private static string? FindString(JsonObject? node, params string[] names)
    {
        if (node is null)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            // 账号资料常见 {} 包一层。
            if (node[name] is JsonObject inner)
            {
                foreach (var key in names)
                {
                    if (inner[key] is JsonValue nested && nested.TryGetValue<string>(out var nestedText) && !string.IsNullOrWhiteSpace(nestedText))
                    {
                        return nestedText;
                    }
                }
            }
        }

        return null;
    }
}
