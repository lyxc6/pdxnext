namespace PdxLauncher.Core.Online;

using System.Net;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 代理解析：按 <see cref="ProxyModes"/> 的三种模式给出「用哪个代理」。
/// 直接连接什么都不看；跟随系统先读 WinINET 系统代理、再回退环境变量；
/// 手动配置只看 AppSettings.ProxyUrl。实测本机 127.0.0.1:7890 可用。
/// </summary>
public static class ProxyResolver
{
    private static readonly string[] EnvironmentVariableNames =
    {
        "http_proxy",
        "https_proxy",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "all_proxy",
        "ALL_PROXY",
    };

    /// <summary>完整解析一次（不打日志，便于自检与界面回显）。</summary>
    /// <param name="settings">用户设置，可为 null。</param>
    /// <param name="environment">环境变量读取函数，默认读真实环境变量。</param>
    /// <param name="systemProxy">系统代理读取函数，默认读 HKCU WinINET（便于自检注入）。</param>
    public static ProxyResolution Describe(
        AppSettings? settings = null,
        Func<string, string?>? environment = null,
        Func<SystemProxySettings?>? systemProxy = null)
    {
        var mode = ProxyModes.Normalize(settings?.ProxyMode, settings?.ProxyUrl);
        return mode switch
        {
            ProxyModes.System => DescribeSystem(
                environment ?? Environment.GetEnvironmentVariable,
                systemProxy ?? SystemProxySettings.Read),
            ProxyModes.Manual => DescribeManual(settings?.ProxyUrl),
            _ => new ProxyResolution(ProxyModes.Direct, ProxySource.Direct, null, null, null),
        };
    }

    /// <summary>解析出代理 URL（已补齐 scheme）；不需要代理时返回 null。</summary>
    /// <param name="settings">用户设置，可为 null。</param>
    /// <param name="environment">环境变量读取函数，默认读真实环境变量（便于自检注入）。</param>
    /// <param name="systemProxy">系统代理读取函数，默认读 HKCU WinINET（便于自检注入）。</param>
    public static string? ResolveUrl(
        AppSettings? settings = null,
        Func<string, string?>? environment = null,
        Func<SystemProxySettings?>? systemProxy = null) =>
        Describe(settings, environment, systemProxy).Url;

    /// <summary>构造 IWebProxy；直连或地址不可用时返回 null，并把原因写进日志。</summary>
    public static IWebProxy? Resolve(
        AppSettings? settings = null,
        IAppLogger? logger = null,
        Func<string, string?>? environment = null,
        Func<SystemProxySettings?>? systemProxy = null)
    {
        var resolution = Describe(settings, environment, systemProxy);
        Log(resolution, logger);
        return resolution.Proxy;
    }

    private static ProxyResolution DescribeManual(string? rawUrl)
    {
        var url = Normalize(rawUrl);
        if (url is null)
        {
            return new ProxyResolution(ProxyModes.Manual, ProxySource.Manual, null, null, "手动配置未填代理地址，当前直连");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return new ProxyResolution(
                ProxyModes.Manual,
                ProxySource.Manual,
                null,
                null,
                "代理地址非法，忽略并直连: " + url);
        }

        if (uri.Scheme is "socks" or "socks4" or "socks5")
        {
            return new ProxyResolution(
                ProxyModes.Manual,
                ProxySource.Manual,
                null,
                null,
                "SOCKS 代理不受支持（.NET 的 HTTP 客户端只能走 HTTP 代理），已直连: " + url);
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return new ProxyResolution(
                ProxyModes.Manual,
                ProxySource.Manual,
                null,
                null,
                "只支持 http / https 代理，已直连: " + url);
        }

        return new ProxyResolution(
            ProxyModes.Manual,
            ProxySource.Manual,
            url,
            CreateProxy(uri, (false, Array.Empty<string>())),
            null);
    }

    private static ProxyResolution DescribeSystem(
        Func<string, string?> read,
        Func<SystemProxySettings?> readSystem)
    {
        var system = readSystem();
        if (system is not null)
        {
            var bypass = ProxyBypassPatterns.Parse(system.BypassList);

            if (system.Enabled)
            {
                var endpoint = SelectServerEndpoint(system.Server, out var hasSocks, out var schemesDiffer);
                if (endpoint is null && hasSocks)
                {
                    return new ProxyResolution(
                        ProxyModes.System,
                        ProxySource.SystemSettings,
                        null,
                        null,
                        "系统代理是 SOCKS 代理，.NET 的 HTTP 客户端不支持，已直连");
                }

                var url = Normalize(endpoint);
                if (url is not null
                    && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    && !string.IsNullOrEmpty(uri.Host))
                {
                    return new ProxyResolution(
                        ProxyModes.System,
                        ProxySource.SystemSettings,
                        url,
                        CreateProxy(uri, bypass),
                        schemesDiffer ? "系统代理对 http / https 配置不同，当前只使用 http 那一份" : null);
                }
            }

            // PAC 是「系统要求自动选择代理」的明确信号：不执行脚本，也不该偷偷回退环境变量。
            if (!string.IsNullOrWhiteSpace(system.AutoConfigUrl))
            {
                return new ProxyResolution(
                    ProxyModes.System,
                    ProxySource.SystemSettings,
                    null,
                    null,
                    "系统配置了 PAC 自动代理脚本（" + system.AutoConfigUrl.Trim() + "），.NET 不执行 PAC，已直连");
            }
        }

        foreach (var name in EnvironmentVariableNames)
        {
            var url = Normalize(read(name));
            if (url is null)
            {
                continue;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && !string.IsNullOrEmpty(uri.Host)
                && uri.Scheme is "http" or "https")
            {
                return new ProxyResolution(
                    ProxyModes.System,
                    ProxySource.EnvironmentVariable,
                    url,
                    CreateProxy(uri, (false, Array.Empty<string>())),
                    null);
            }

            return new ProxyResolution(
                ProxyModes.System,
                ProxySource.EnvironmentVariable,
                null,
                null,
                "环境变量里的代理地址不可用（只支持 http / https），已直连: " + url);
        }

        return new ProxyResolution(ProxyModes.System, ProxySource.SystemSettings, null, null, null);
    }

    /// <summary>
    /// 从 ProxyServer 里挑一个 HTTP 端点。
    /// 支持 127.0.0.1:7890 与 http=a:1;https=b:2 两种写法：优先 http=，没有再用 https=。
    /// </summary>
    private static string? SelectServerEndpoint(string? server, out bool hasSocks, out bool schemesDiffer)
    {
        hasSocks = false;
        schemesDiffer = false;
        if (string.IsNullOrWhiteSpace(server))
        {
            return null;
        }

        var entries = server.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            return null;
        }

        // 单值写法（Clash 常见）：127.0.0.1:7890，没有 "key=" 前缀。
        if (entries.Length == 1 && !entries[0].Contains('=', StringComparison.Ordinal))
        {
            hasSocks = entries[0].StartsWith("socks", StringComparison.OrdinalIgnoreCase);
            return hasSocks ? null : entries[0];
        }

        string? http = null;
        string? https = null;
        foreach (var entry in entries)
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var scheme = entry[..separator].Trim();
            var endpoint = entry[(separator + 1)..].Trim();
            if (endpoint.Length == 0)
            {
                continue;
            }

            if (scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase))
            {
                hasSocks = true;
                continue;
            }

            if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                http = endpoint;
            }
            else if (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                https = endpoint;
            }
        }

        schemesDiffer = http is not null
            && https is not null
            && !string.Equals(http, https, StringComparison.OrdinalIgnoreCase);

        return http ?? https;
    }

    private static IWebProxy CreateProxy(Uri proxyUri, (bool BypassOnLocal, IReadOnlyList<string> Regexes) bypass)
    {
        var proxy = new WebProxy(proxyUri);
        proxy.BypassProxyOnLocal = bypass.BypassOnLocal;
        if (bypass.Regexes.Count > 0)
        {
            // 这里放的是 ProxyBypassPatterns 生成的正则，WebProxy 会按正则匹配目标主机。
            proxy.BypassList = bypass.Regexes.ToArray();
        }

        // 地址里带 user:pass@ 时用它做代理凭据；否则交给系统默认凭据（DefaultProxyCredentials）。
        var userInfo = proxyUri.UserInfo;
        if (!string.IsNullOrEmpty(userInfo))
        {
            var separator = userInfo.IndexOf(':');
            proxy.Credentials = separator < 0
                ? new NetworkCredential(Uri.UnescapeDataString(userInfo), string.Empty)
                : new NetworkCredential(
                    Uri.UnescapeDataString(userInfo[..separator]),
                    Uri.UnescapeDataString(userInfo[(separator + 1)..]));
        }

        return proxy;
    }

    private static void Log(ProxyResolution resolution, IAppLogger? logger)
    {
        if (logger is null)
        {
            return;
        }

        if (resolution.Warning is not null)
        {
            logger.Warn(resolution.Warning);
        }

        if (resolution.Url is null)
        {
            if (resolution.Warning is null)
            {
                logger.Info(resolution.Mode == ProxyModes.System ? "系统未配置代理，直连" : "未配置代理，直连");
            }

            return;
        }

        var authority = Uri.TryCreate(resolution.Url, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : resolution.Url;

        logger.Info(resolution.Source switch
        {
            ProxySource.SystemSettings => "跟随系统代理 " + authority,
            ProxySource.EnvironmentVariable => "使用环境变量代理 " + authority,
            _ => "使用代理 " + authority,
        });
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().Trim('"', '\'').Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // 环境变量与系统代理常见写法是 127.0.0.1:7890（无 scheme），补成 http://
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return "http://" + trimmed;
    }
}
