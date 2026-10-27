namespace PdxLauncher.Core.Online;

using System.Net;

/// <summary>代理从哪里来（只用于界面上「当前生效」的回显，不写进 settings.json）。</summary>
public enum ProxySource
{
    /// <summary>直连。</summary>
    Direct,

    /// <summary>Windows 系统代理（HKCU WinINET）。</summary>
    SystemSettings,

    /// <summary>http_proxy/https_proxy 等环境变量。</summary>
    EnvironmentVariable,

    /// <summary>设置页手填的地址。</summary>
    Manual,
}

/// <summary>一次代理解析的完整结果：模式、来源、地址、可直接交给 HttpClient 的代理对象和中文原因。</summary>
/// <param name="Mode">归一化后的代理模式，取值见 <see cref="Configuration.ProxyModes"/>。</param>
/// <param name="Source">地址取自哪里。</param>
/// <param name="Url">已补齐 scheme 的代理地址；直连时为 null。</param>
/// <param name="Proxy">非 null 表示要用代理；null 表示直连。</param>
/// <param name="Warning">面向用户的中文原因（非法地址 / SOCKS / PAC / 手动未填）；正常时为 null。</param>
public sealed record ProxyResolution(
    string Mode,
    ProxySource Source,
    string? Url,
    IWebProxy? Proxy,
    string? Warning);
