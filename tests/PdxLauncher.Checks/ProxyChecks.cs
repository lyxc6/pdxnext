namespace PdxLauncher.Checks;

using System.Net;
using System.Text.RegularExpressions;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;

/// <summary>
/// 网络代理自检（不联网）：三模式归一化、旧配置迁移、系统代理 / 环境变量回落顺序、
/// SOCKS / PAC / 非法地址的兜底文案，以及 ProxyOverride → 正则的翻译。
/// </summary>
public static class ProxyChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        CheckModes();
        CheckManual();
        CheckSystem();
        CheckBypassPatterns();
        CheckLogging();
        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ 模式归一化

    private static void CheckModes()
    {
        ChecksContext.Equal("代理模式: 都没填 → 直连", ProxyModes.Direct, ProxyModes.Normalize(null, null));
        ChecksContext.Equal("代理模式: 空串 → 直连", ProxyModes.Direct, ProxyModes.Normalize("   ", " "));

        // 旧版本只写了 proxyUrl、没有 proxyMode：保持原来的「手动配置」行为。
        ChecksContext.Equal(
            "代理模式: 旧配置只有地址 → 手动配置",
            ProxyModes.Manual,
            ProxyModes.Normalize(null, "127.0.0.1:7890"));

        ChecksContext.Equal("代理模式: system", ProxyModes.System, ProxyModes.Normalize("system", null));
        ChecksContext.Equal("代理模式: SYSTEM 大小写不敏感", ProxyModes.System, ProxyModes.Normalize(" SYSTEM ", null));
        ChecksContext.Equal("代理模式: direct 优先于地址", ProxyModes.Direct, ProxyModes.Normalize("direct", "127.0.0.1:7890"));
        ChecksContext.Equal("代理模式: 未知值 + 有地址 → 手动配置", ProxyModes.Manual, ProxyModes.Normalize("bogus", "127.0.0.1:7890"));
        ChecksContext.Equal("代理模式: 未知值 → 直连", ProxyModes.Direct, ProxyModes.Normalize("bogus", null));

        ChecksContext.Check("代理模式: IsManual 判定", ProxyModes.IsManual("manual", null) && !ProxyModes.IsManual("system", "http://a"));
    }

    // ------------------------------------------------------------ 手动配置

    private static void CheckManual()
    {
        var noEnv = new Func<string, string?>(_ => null);
        Func<SystemProxySettings?> noSystem = () => null;

        var bare = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.Manual, ProxyUrl = " 127.0.0.1:7890 " },
            noEnv,
            noSystem);
        ChecksContext.Equal("手动配置: 补 scheme", "http://127.0.0.1:7890", bare.Url);
        ChecksContext.Equal("手动配置: 来源", ProxySource.Manual, bare.Source);
        ChecksContext.Check("手动配置: 有 Warning 时不该有地址", bare.Warning is null);

        var quoted = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.Manual, ProxyUrl = "\"http://127.0.0.1:7890\"" },
            noEnv,
            noSystem);
        ChecksContext.Equal("手动配置: 去掉引号", "http://127.0.0.1:7890", quoted.Url);

        var https = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.Manual, ProxyUrl = "https://proxy.example:8443" },
            noEnv,
            noSystem);
        ChecksContext.Equal("手动配置: https 代理可用", "https://proxy.example:8443", https.Url);

        // 空地址：手动模式下要明确告诉用户「当前直连」，而不是静默。
        var empty = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.Manual, ProxyUrl = "   " },
            noEnv,
            noSystem);
        ChecksContext.Check("手动配置: 空地址直连", empty.Url is null && empty.Proxy is null);
        ChecksContext.Check(
            "手动配置: 空地址给提示",
            empty.Warning is not null && empty.Warning.Contains("直连", StringComparison.Ordinal),
            empty.Warning ?? "<null>");

        // SOCKS 与非法 scheme 都不支持，但要留下原因。
        var socks = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.Manual, ProxyUrl = "socks5://127.0.0.1:1080" },
            noEnv,
            noSystem);
        ChecksContext.Check("手动配置: SOCKS 直连", socks.Url is null);
        ChecksContext.Check(
            "手动配置: SOCKS 说明原因",
            socks.Warning is not null && socks.Warning.Contains("SOCKS", StringComparison.Ordinal),
            socks.Warning ?? "<null>");

        var ftp = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.Manual, ProxyUrl = "ftp://127.0.0.1:7890" },
            noEnv,
            noSystem);
        ChecksContext.Check("手动配置: 非 http scheme 直连", ftp.Url is null);
        ChecksContext.Check(
            "手动配置: 非 http scheme 说明原因",
            ftp.Warning is not null && ftp.Warning.Contains("http", StringComparison.Ordinal),
            ftp.Warning ?? "<null>");

        // 带账号密码时用 NetworkCredential，不能把 user:pass 留在 URL 里当主机。
        var withAuth = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.Manual, ProxyUrl = "http://user:p%40ss@127.0.0.1:7890" },
            noEnv,
            noSystem);
        ChecksContext.Check("手动配置: 带认证仍构造出 WebProxy", withAuth.Proxy is WebProxy);
        ChecksContext.Check(
            "手动配置: 认证被解析成凭据",
            withAuth.Proxy is WebProxy auth
                && auth.Credentials is NetworkCredential credential
                && credential.UserName == "user"
                && credential.Password == "p@ss");
    }

    // ------------------------------------------------------------ 跟随系统

    private static void CheckSystem()
    {
        var systemEnabled = new Func<SystemProxySettings?>(
            () => new SystemProxySettings(true, "127.0.0.1:7890", null, null));
        var env = new Func<string, string?>(name =>
            string.Equals(name, "http_proxy", StringComparison.OrdinalIgnoreCase) ? "http://10.0.0.1:3128" : null);

        var fromSystem = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            env,
            systemEnabled);
        ChecksContext.Equal("跟随系统: 系统代理优先", "http://127.0.0.1:7890", fromSystem.Url);
        ChecksContext.Equal("跟随系统: 来源是系统设置", ProxySource.SystemSettings, fromSystem.Source);

        // ProxyEnable=0（用户在 Windows 里关掉了代理）→ 回落到环境变量。
        var disabled = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            env,
            () => new SystemProxySettings(false, "127.0.0.1:7890", null, null));
        ChecksContext.Equal("跟随系统: 系统代理关闭时用环境变量", "http://10.0.0.1:3128", disabled.Url);
        ChecksContext.Equal("跟随系统: 来源是环境变量", ProxySource.EnvironmentVariable, disabled.Source);

        // http=a;https=b 写法：优先 http，并提示「只用了其中一份」。
        var split = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            _ => null,
            () => new SystemProxySettings(true, "http=127.0.0.1:7890;https=127.0.0.1:7891", null, null));
        ChecksContext.Equal("跟随系统: 分开配置时取 http", "http://127.0.0.1:7890", split.Url);
        ChecksContext.Check(
            "跟随系统: 分开配置给提示",
            split.Warning is not null && split.Warning.Contains("不同", StringComparison.Ordinal),
            split.Warning ?? "<null>");

        var httpsOnly = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            _ => null,
            () => new SystemProxySettings(true, "https=127.0.0.1:7891", null, null));
        ChecksContext.Equal("跟随系统: 只有 https 时用它", "http://127.0.0.1:7891", httpsOnly.Url);

        // SOCKS 系统代理：.NET 的 HTTP 客户端不支持，必须直连并说明。
        var socksSystem = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            env,
            () => new SystemProxySettings(true, "socks=127.0.0.1:1080", null, null));
        ChecksContext.Check("跟随系统: SOCKS 系统代理直连", socksSystem.Url is null);
        ChecksContext.Check(
            "跟随系统: SOCKS 说明原因",
            socksSystem.Warning is not null && socksSystem.Warning.Contains("SOCKS", StringComparison.Ordinal),
            socksSystem.Warning ?? "<null>");

        // PAC：明确不执行，也不偷偷回退环境变量（否则用户看到的和实际走的不是一个代理）。
        var pac = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            env,
            () => new SystemProxySettings(false, null, null, "http://pac.example/proxy.pac"));
        ChecksContext.Check("跟随系统: PAC 直连", pac.Url is null);
        ChecksContext.Check(
            "跟随系统: PAC 说明原因",
            pac.Warning is not null && pac.Warning.Contains("PAC", StringComparison.Ordinal),
            pac.Warning ?? "<null>");

        // 系统既没配代理也没有 PAC → 环境变量；环境变量是 SOCKS 也要拒绝。
        var envSocks = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            name => string.Equals(name, "all_proxy", StringComparison.OrdinalIgnoreCase) ? "socks5://127.0.0.1:1080" : null,
            () => new SystemProxySettings(false, null, null, null));
        ChecksContext.Check("跟随系统: 环境变量 SOCKS 直连", envSocks.Url is null);
        ChecksContext.Check(
            "跟随系统: 环境变量 SOCKS 说明原因",
            envSocks.Warning is not null && envSocks.Warning.Contains("环境变量", StringComparison.Ordinal),
            envSocks.Warning ?? "<null>");

        // 全都没有：仍然是 System 模式、无地址、无警告（日志里说「系统未配置代理，直连」）。
        var nothing = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            _ => null,
            () => null);
        ChecksContext.Check("跟随系统: 都没有则直连", nothing.Url is null && nothing.Warning is null);
        ChecksContext.Equal("跟随系统: 模式保持不变", ProxyModes.System, nothing.Mode);

        // 系统代理带 ProxyOverride：本地地址与手写通配都要真的绕过。
        var withBypass = ProxyResolver.Describe(
            new AppSettings { ProxyMode = ProxyModes.System },
            _ => null,
            () => new SystemProxySettings(true, "127.0.0.1:7890", "localhost;*.internal;<local>", null));
        ChecksContext.Check(
            "跟随系统: ProxyOverride 变成 Bypass 规则",
            withBypass.Proxy is WebProxy bypass
                && bypass.BypassProxyOnLocal
                && bypass.IsBypassed(new Uri("http://api.internal/"))
                && bypass.IsBypassed(new Uri("http://localhost:8080/")),
            withBypass.Url ?? "<null>");
    }

    // ------------------------------------------------------------ ProxyOverride

    private static void CheckBypassPatterns()
    {
        var empty = ProxyBypassPatterns.Parse(null);
        ChecksContext.Check("Bypass: null → 空", !empty.BypassOnLocal && empty.Regexes.Count == 0);

        var parsed = ProxyBypassPatterns.Parse(" localhost ;127.*;*.corp.example;<LOCAL>;");
        ChecksContext.Check("Bypass: 识别 <local>", parsed.BypassOnLocal);
        ChecksContext.Equal("Bypass: 项数", 3, parsed.Regexes.Count);
        ChecksContext.Check(
            "Bypass: localhost 精确匹配",
            Regex.IsMatch("localhost", parsed.Regexes[0]) && !Regex.IsMatch("localhost6", parsed.Regexes[0]));
        ChecksContext.Check(
            "Bypass: 127.* 前缀匹配",
            Regex.IsMatch("127.0.0.1", parsed.Regexes[1]) && !Regex.IsMatch("128.0.0.1", parsed.Regexes[1]));
        ChecksContext.Check(
            "Bypass: *.corp.example 通配",
            Regex.IsMatch("a.corp.example", parsed.Regexes[2]) && !Regex.IsMatch("corp.example.evil", parsed.Regexes[2]));

        // 正则元字符必须被转义，否则 a+b 这种主机名会匹配到 aab。
        var escaped = ProxyBypassPatterns.Parse("a+b.example");
        ChecksContext.Check(
            "Bypass: 元字符按字面量",
            escaped.Regexes.Count == 1
                && Regex.IsMatch("a+b.example", escaped.Regexes[0])
                && !Regex.IsMatch("aab.example", escaped.Regexes[0]),
            string.Join(",", escaped.Regexes));
    }

    // ------------------------------------------------------------ 日志

    private static void CheckLogging()
    {
        var logger = new RecordingLogger();
        ProxyResolver.Resolve(
            new AppSettings { ProxyMode = ProxyModes.System },
            logger,
            _ => null,
            () => null);
        ChecksContext.Check(
            "日志: 系统未配置代理写一条 info",
            logger.Infos.Any(i => i.Contains("系统未配置代理", StringComparison.Ordinal) && i.Contains("直连", StringComparison.Ordinal)),
            string.Join(" | ", logger.Infos));

        var manualLogger = new RecordingLogger();
        ProxyResolver.Resolve(
            new AppSettings { ProxyMode = ProxyModes.Manual, ProxyUrl = "127.0.0.1:7890" },
            manualLogger,
            _ => null,
            () => null);
        ChecksContext.Check(
            "日志: 手动配置写「使用代理」",
            manualLogger.Infos.Any(i => i.Contains("使用代理", StringComparison.Ordinal) && i.Contains("127.0.0.1:7890", StringComparison.Ordinal)),
            string.Join(" | ", manualLogger.Infos));

        // 直连模式不应该产生任何代理相关日志（避免每次启动都刷屏）。
        var directLogger = new RecordingLogger();
        ProxyResolver.Resolve(
            new AppSettings { ProxyMode = ProxyModes.Direct },
            directLogger,
            _ => null,
            () => null);
        ChecksContext.Check(
            "日志: 直连只写一条",
            directLogger.Infos.Count == 1 && directLogger.Warnings.Count == 0,
            string.Join(" | ", directLogger.Infos));
    }

    // ------------------------------------------------------------ 测试替身

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Infos { get; } = new();

        public List<string> Warnings { get; } = new();

        public List<string> Errors { get; } = new();

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }
}
