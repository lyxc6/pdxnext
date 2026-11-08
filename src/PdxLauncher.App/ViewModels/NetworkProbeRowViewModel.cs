namespace PdxLauncher.App.ViewModels;

/// <summary>
/// 设置页连通性自检的一行：一台主机在两个模式下各是什么结果。
/// 文案在 <see cref="SettingsViewModel"/> 里按当前语言拼好；语言切换后整批重建。
/// </summary>
public sealed class NetworkProbeRowViewModel
{
    public NetworkProbeRowViewModel(string key, string host, string currentText, string directText, string automationId)
    {
        Key = key;
        Host = host;
        CurrentText = currentText;
        DirectText = directText;
        AutomationId = automationId;
    }

    /// <summary>稳定键（steamcommunity / steamapi / steamstore），用于自动化与排错。</summary>
    public string Key { get; }

    /// <summary>目标主机名。</summary>
    public string Host { get; }

    /// <summary>按当前代理模式测出的结果。</summary>
    public string CurrentText { get; }

    /// <summary>忽略代理直连测出的结果；当前就在直连时为空字符串。</summary>
    public string DirectText { get; }

    /// <summary>有没有直连对比列（当前就在直连时整列不显示）。</summary>
    public bool HasDirectText => DirectText.Length > 0;

    /// <summary>直连那一格的自动化 id；没有直连结果时为空串。</summary>
    public string DirectAutomationId => HasDirectText ? AutomationId + "Direct" : string.Empty;

    public string AutomationId { get; }
}
