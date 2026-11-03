namespace PdxLauncher.Checks;

using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Contracts;

/// <summary>
/// Paradox 账号登录自检：密码哈希向量、请求形状、失败映射、响应解析、落盘策略、表单判定。
/// <para>
/// 为什么要钉这些：登录链路的每一环都是「静默失败」——请求发错形状服务端只回 401、
/// 会话解析少取一个字段界面就永远显示未登录、不勾「保持登录」却照旧落盘则文案在说谎。
/// 这些都不会崩，只会让人以为「登录坏了」，所以必须由自检钉住。
/// </para>
/// </summary>
public static class AccountChecks
{
    /// <summary>由 <see cref="Program"/> 反射调用。</summary>
    /// <param name="fs">真实文件系统（仅用于临时目录，不碰真实 data\\pdx-session.json）。</param>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckPasswordHash();
        CheckRequestShape();
        CheckFailureMapping();
        CheckSessionParsing();
        CheckPersistence(fs);
        CheckFormState();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ---------------------------------------------------------------- 哈希

    private static void CheckPasswordHash()
    {
        // 向量由 python hashlib 独立验算，避免「照抄实现」式的自证。
        ChecksContext.Equal(
            "账号: 密码哈希向量(secret)",
            "d9b6a2f0386aa68a5c755609302167589e36d3216dd859c94fa73cf3c8bc50ac",
            PdxPassword.Hash("secret"));
        ChecksContext.Equal(
            "账号: 密码哈希向量(空密码)",
            "0e368a11c2183a7f2a8620b5a0dc4f5aeca0784cc6e63e05e5965fbaa945b51b",
            PdxPassword.Hash(string.Empty));
        ChecksContext.Equal(
            "账号: 密码哈希向量(特殊字符)",
            "09ad111b75122f1071bde48874e98bfedcb3338fbf50242b8fa5828725365a1e",
            PdxPassword.Hash("P@ssw0rd!"));
        ChecksContext.Equal("账号: 盐与官方一致", "64DC4DC6508F4A9D823179D4D99DB4EE", PdxPassword.Salt);
        ChecksContext.Equal("账号: 头键名与官方一致", "sha256(password+salt)", PdxPassword.HeaderKeyName);
    }

    // ---------------------------------------------------------------- 请求

    private static void CheckRequestShape()
    {
        var transport = new FakeTransport(401, "{\"result\":\"Failure\",\"errorCode\":\"not-authorized\"}");
        var client = NewClient(transport);

        var result = client.SignInAsync("  Player@Example.COM  ", "secret", rememberMe: true, CancellationToken.None)
            .GetAwaiter().GetResult();

        ChecksContext.Check("账号: 假传输被调用一次", transport.Calls.Count == 1, transport.Calls.Count.ToString(CultureInfo.InvariantCulture));
        if (transport.Calls.Count != 1)
        {
            return;
        }

        var call = transport.Calls[0];
        ChecksContext.Equal("账号: 登录用 PUT", "PUT", call.Method);
        ChecksContext.Equal("账号: 登录端点", PdxAccountClient.SessionEndpoint.ToString(), call.Uri.ToString());
        ChecksContext.Check("账号: 登录 body 为空", call.JsonBody is null, call.JsonBody);

        var root = JsonNode.Parse(call.AuthorizationJson ?? "null") as JsonObject;
        var hawk = root?["hawk"] as JsonObject;
        ChecksContext.Check("账号: 头里是 hawk", hawk is not null, call.AuthorizationJson);
        ChecksContext.Equal("账号: 邮箱已去空格", "Player@Example.COM", hawk?["email"]?.GetValue<string>());
        ChecksContext.Equal(
            "账号: 头里带密码哈希",
            PdxPassword.Hash("secret"),
            hawk?[PdxPassword.HeaderKeyName]?.GetValue<string>());
        ChecksContext.Check(
            "账号: 头里不含明文密码",
            call.AuthorizationJson?.Contains("secret", StringComparison.Ordinal) != true,
            call.AuthorizationJson);

        // 401 时应映射成 Unauthorized + 固定错误码，界面据此翻成「邮箱或密码不正确」。
        ChecksContext.Equal("账号: 401 → 失败", false, result.IsSuccess);
        ChecksContext.Equal("账号: 401 → Unauthorized", ErrorKind.Unauthorized, result.Kind);
        ChecksContext.Equal("账号: 401 → 错误码", "pdx-login-unauthorized", result.ErrorMessage);
    }

    private static void CheckFailureMapping()
    {
        var serverError = NewClient(new FakeTransport(500, "{\"errorCode\":\"boom\"}"));
        var failed = serverError.SignInAsync("a@b.c", "secret", rememberMe: false, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("账号: 500 → 失败", false, failed.IsSuccess);
        ChecksContext.Equal("账号: 500 → Io", ErrorKind.Io, failed.Kind);
        ChecksContext.Equal("账号: 500 → 错误码带状态", "pdx-login-failed:500", failed.ErrorMessage);

        var offline = NewClient(new FakeTransport(new HttpRequestException("dns")));
        var network = offline.SignInAsync("a@b.c", "secret", rememberMe: false, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("账号: 网络异常 → 错误码", "pdx-login-network", network.ErrorMessage);
        ChecksContext.Equal("账号: 网络异常 → Io", ErrorKind.Io, network.Kind);
    }

    // ---------------------------------------------------------------- 解析

    private static void CheckSessionParsing()
    {
        const string body = "{\"session\":{\"token\":\"TK\",\"userid\":\"UID\",\"refresh_token\":\"RT\"},\"displayName\":\"Player\"}";
        var client = NewClient(new FakeTransport(200, body));
        var nested = client.SignInAsync("a@b.c", "secret", rememberMe: true, CancellationToken.None)
            .GetAwaiter().GetResult();

        ChecksContext.Check("账号: 嵌套 session 解析成功", nested.IsSuccess, nested.ErrorMessage);
        ChecksContext.Equal("账号: 取到 token", "TK", nested.Value?.SessionToken);
        ChecksContext.Equal("账号: 取到 userid", "UID", nested.Value?.UserId);
        ChecksContext.Equal("账号: 取到 refresh_token", "RT", nested.Value?.RefreshToken);
        ChecksContext.Equal("账号: 取到显示名", "Player", nested.Value?.DisplayName);
        ChecksContext.Check("账号: 有 refresh → 可续期", nested.Value?.CanRefresh == true);

        // 顶层扁平的 token 也要能读（不同 client 的响应形状不完全一致）。
        var flatClient = NewClient(new FakeTransport(200, "{\"token\":\"TK2\",\"userid\":\"U2\"}"));
        var flat = flatClient.SignInAsync("a@b.c", "secret", rememberMe: false, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("账号: 扁平 token 解析成功", "TK2", flat.Value?.SessionToken);

        // 没有 token 就是没登录，不能假装成功。
        var emptyClient = NewClient(new FakeTransport(200, "{\"result\":\"Failure\"}"));
        var missing = emptyClient.SignInAsync("a@b.c", "secret", rememberMe: false, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("账号: 缺 token → 失败", false, missing.IsSuccess);
        ChecksContext.Equal("账号: 缺 token → 错误码", "pdx-login-no-token", missing.ErrorMessage);
    }

    // ---------------------------------------------------------------- 落盘

    private static void CheckPersistence(IFileSystem fs)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pdx-account-check-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, PdxSessionStore.FileName);
        const string body = "{\"session\":{\"token\":\"TK\",\"userid\":\"UID\",\"refresh_token\":\"RT\"}}";

        try
        {
            var store = new PdxSessionStore(fs, new SilentLogger(), new FakeProtector(), path);
            var service = new AccountService(new SilentLogger(), store, new FixedClock(), NewClient(new FakeTransport(200, body)));

            // 不勾「保持登录」：只在本次运行有效，磁盘上不能留东西
            // （与 account.rememberHint 的承诺一致；否则重启后会被 RestoreAsync 又恢复出来）。
            var remembered = service.SignInAsync("a@b.c", "secret", rememberMe: false, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("账号: 不勾保持登录也能登录", remembered.IsSuccess, remembered.ErrorMessage);
            ChecksContext.Check("账号: 不勾保持登录 → 内存有会话", service.Session is not null);
            ChecksContext.Check("账号: 不勾保持登录 → 不落盘", !fs.FileExists(path));

            service.SignOut();
            ChecksContext.Check("账号: 登出后内存会话清空", service.Session is null);

            // 勾了才落盘，且能读回来。
            var keepService = new AccountService(new SilentLogger(), store, new FixedClock(), NewClient(new FakeTransport(200, body)));
            var kept = keepService.SignInAsync("a@b.c", "secret", rememberMe: true, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("账号: 勾保持登录登录成功", kept.IsSuccess, kept.ErrorMessage);
            ChecksContext.Check("账号: 勾保持登录 → 落盘", fs.FileExists(path));

            var reloaded = store.Load();
            ChecksContext.Equal("账号: 落盘后能读回 token", "TK", reloaded?.SessionToken);
            ChecksContext.Equal("账号: 落盘后能读回邮箱", "a@b.c", reloaded?.Email);

            // 登录之后再改「保持登录」开关也必须真的生效，否则这个开关只在登录那一刻有意义：
            // 用户改完看不到任何变化，下次启动照样自动续期（或者照样要重登）。
            keepService.ApplyRememberPreference(false);
            ChecksContext.Check("账号: 登录后关掉保持登录 → 会话文件被清掉", !fs.FileExists(path));

            keepService.ApplyRememberPreference(true);
            ChecksContext.Check("账号: 登录后重新打开保持登录 → 会话重新落盘", fs.FileExists(path));
            ChecksContext.Equal("账号: 重新落盘后仍能读回 token", "TK", store.Load()?.SessionToken);

            // 再以「不勾」登录一次，应把上一次的会话文件清掉。
            var clearService = new AccountService(new SilentLogger(), store, new FixedClock(), NewClient(new FakeTransport(200, body)));
            clearService.SignInAsync("a@b.c", "secret", rememberMe: false, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("账号: 改回不勾 → 旧会话文件被清掉", !fs.FileExists(path));

            // 未登录时改开关只是记偏好，不能凭空造出一个会话文件。
            var signedOut = new AccountService(new SilentLogger(), store, new FixedClock(), NewClient(new FakeTransport(200, body)));
            signedOut.ApplyRememberPreference(true);
            ChecksContext.Check("账号: 未登录时改保持登录 → 不写会话文件", !fs.FileExists(path));
        }
        finally
        {
            if (fs.FileExists(path))
            {
                fs.Delete(path);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------- 表单

    private static void CheckFormState()
    {
        // 回归钉子：曾经的死锁就是「邮箱为空 → 输入框自己禁用自己 → 永远输不进邮箱」。
        ChecksContext.Check("表单: 空闲未登录时可编辑（回归钉）", AccountFormState.CanEditCredentials(false, false));
        ChecksContext.Check("表单: 请求进行中不可编辑", !AccountFormState.CanEditCredentials(true, false));
        ChecksContext.Check("表单: 已登录时不可编辑", !AccountFormState.CanEditCredentials(false, true));

        ChecksContext.Check("表单: 空邮箱不能提交", !AccountFormState.CanSubmit(false, false, string.Empty));
        ChecksContext.Check("表单: 纯空白邮箱不能提交", !AccountFormState.CanSubmit(false, false, "   "));
        ChecksContext.Check("表单: null 邮箱不能提交", !AccountFormState.CanSubmit(false, false, null));
        ChecksContext.Check("表单: 有邮箱可提交", AccountFormState.CanSubmit(false, false, "a@b.c"));
        ChecksContext.Check("表单: 请求进行中即使有邮箱也不能提交", !AccountFormState.CanSubmit(true, false, "a@b.c"));

        // 关键性质：可编辑性完全不含「邮箱是否填写」这个因子 —— 这正是死锁的解法。
        foreach (var email in new[] { string.Empty, "   ", "a@b.c" })
        {
            var label = email.Length == 0 ? "空" : email.Trim().Length == 0 ? "空白" : "有值";
            ChecksContext.Equal(
                "表单: 可编辑性与邮箱无关(" + label + ")",
                true,
                AccountFormState.CanEditCredentials(false, false));
            ChecksContext.Equal(
                "表单: 可提交性 = 可编辑 且 邮箱非空(" + label + ")",
                !string.IsNullOrWhiteSpace(email),
                AccountFormState.CanSubmit(false, false, email));
        }

        CheckErrorClassification();
    }

    // ---------------------------------------------------------------- 归类

    private static void CheckErrorClassification()
    {
        // 回归钉子：真机上「密码错了」显示成「登录失败：pdx-login-unauthorized」，
        // 用户完全看不出是自己密码打错 —— 就因为这族码没被认出来。
        ChecksContext.Equal(
            "归类: pdx-login-unauthorized → 凭据错",
            AccountErrorCategory.BadCredentials,
            AccountErrorText.Describe("pdx-login-unauthorized"));
        ChecksContext.Equal(
            "归类: pdx-refresh-unauthorized → 凭据错",
            AccountErrorCategory.BadCredentials,
            AccountErrorText.Describe("pdx-refresh-unauthorized"));
        ChecksContext.Equal(
            "归类: pdx-login-failed:401 → 凭据错",
            AccountErrorCategory.BadCredentials,
            AccountErrorText.Describe("pdx-login-failed:401"));
        ChecksContext.Equal(
            "归类: pdx-login-failed:403 → 凭据错",
            AccountErrorCategory.BadCredentials,
            AccountErrorText.Describe("pdx-login-failed:403"));
        ChecksContext.Equal(
            "归类: pdx-login-network → 网络",
            AccountErrorCategory.Network,
            AccountErrorText.Describe("pdx-login-network"));
        ChecksContext.Equal(
            "归类: timeout → 网络",
            AccountErrorCategory.Network,
            AccountErrorText.Describe("timeout"));
        ChecksContext.Equal(
            "归类: pdx-login-failed:500 → 其它（保留原始码）",
            AccountErrorCategory.Other,
            AccountErrorText.Describe("pdx-login-failed:500"));
        ChecksContext.Equal(
            "归类: null → 其它",
            AccountErrorCategory.Other,
            AccountErrorText.Describe(null));
    }

    // ---------------------------------------------------------------- 假件

    private static PdxAccountClient NewClient(FakeTransport transport)
        => new(transport, new SilentLogger(), new FixedClock());

    /// <summary>记录调用并按预设状态码回应的传输。</summary>
    private sealed class FakeTransport : IPdxHttpTransport
    {
        private readonly int _status;
        private readonly string _body;
        private readonly Exception? _throw;

        public FakeTransport(int status, string body)
        {
            _status = status;
            _body = body;
        }

        public FakeTransport(Exception toThrow)
        {
            _throw = toThrow;
            _body = string.Empty;
        }

        public List<Call> Calls { get; } = new();

        public Task<PdxHttpResponse> SendAsync(
            string method,
            Uri uri,
            string? authorizationJson,
            string? jsonBody,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Calls.Add(new Call(method, uri, authorizationJson, jsonBody, headers));
            if (_throw is not null)
            {
                throw _throw;
            }

            return Task.FromResult(new PdxHttpResponse(_status, _body));
        }

        public sealed record Call(
            string Method,
            Uri Uri,
            string? AuthorizationJson,
            string? JsonBody,
            IReadOnlyDictionary<string, string>? Headers = null);
    }

    /// <summary>可逆的假保护器：自检不依赖 DPAPI（检查工程跑在 net10.0、也可能在别的机器上）。</summary>
    private sealed class FakeProtector : ISecretProtector
    {
        private const string Prefix = "fake:";

        public string Scheme => "fake";

        public string Protect(string plaintext) => Prefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

        public string? Unprotect(string protectedText)
            => protectedText.StartsWith(Prefix, StringComparison.Ordinal)
                ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[Prefix.Length..]))
                : null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
