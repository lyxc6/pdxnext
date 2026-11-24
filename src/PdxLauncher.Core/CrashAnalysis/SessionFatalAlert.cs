namespace PdxLauncher.Core.CrashAnalysis;

/// <summary>本次会话致命错误提醒的一次性闸门：纯状态判定，不碰 UI 与文件。</summary>
public sealed class SessionFatalAlert
{
    private string? _announcedSignature;

    /// <summary>
    /// 快照里有新的致命错误时返回 true，并且同一签名整次会话只返回一次。
    /// </summary>
    public bool ShouldAnnounce(GameSessionSnapshot? snapshot)
    {
        var fatal = snapshot?.FatalInfo;
        if (fatal is null || string.Equals(_announcedSignature, fatal.Signature, StringComparison.Ordinal))
        {
            return false;
        }

        _announcedSignature = fatal.Signature;
        return true;
    }

    /// <summary>新会话开始时重置。</summary>
    public void Reset() => _announcedSignature = null;
}
