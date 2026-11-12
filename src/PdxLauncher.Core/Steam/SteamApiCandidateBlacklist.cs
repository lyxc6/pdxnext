namespace PdxLauncher.Core.Steam;

/// <summary>
/// 进程内的「不可用候选」黑名单：某份 <c>steam_api64.dll</c> 把宿主进程崩掉（0xC0000005 之类）、
/// 或者在 <c>SteamAPI_Init</c> 里失败（典型的 DLC 解锁 loader），本次运行就不要再拿它试，
/// 后续成就读取直接跳过。
/// <para>
/// 只存路径与原因、不写盘：重启后重新试一次是合理的（玩家可能修好了 DLL），
/// 而同一进程里反复拿同一个文件去崩 / 去等一次 Init 失败是纯粹的浪费。
/// </para>
/// </summary>
public static class SteamApiCandidateBlacklist
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> Blocked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>这个路径是否已被标记为不可用。</summary>
    /// <param name="path">DLL 路径。</param>
    public static bool IsBlocked(string path)
    {
        lock (Gate)
        {
            return Blocked.ContainsKey(path);
        }
    }

    /// <summary>取标记原因；未标记时返回 false。</summary>
    /// <param name="path">DLL 路径。</param>
    /// <param name="reason">标记原因。</param>
    public static bool TryGetReason(string path, out string? reason)
    {
        lock (Gate)
        {
            return Blocked.TryGetValue(path, out reason);
        }
    }

    /// <summary>把路径拉黑并记下原因；已在名单里时返回 false（保留第一次的原因）。</summary>
    /// <param name="path">DLL 路径。</param>
    /// <param name="reason">原因，写进日志与后续跳过提示。</param>
    public static bool MarkBlocked(string path, string reason)
    {
        lock (Gate)
        {
            if (Blocked.ContainsKey(path))
            {
                return false;
            }

            Blocked[path] = reason;
            return true;
        }
    }

    /// <summary>清空名单（自检用；正常运行期不该调用）。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Blocked.Clear();
        }
    }
}
