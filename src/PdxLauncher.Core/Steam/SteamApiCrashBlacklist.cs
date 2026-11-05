namespace PdxLauncher.Core.Steam;

/// <summary>
/// 进程内的「崩溃候选」黑名单：某份 <c>steam_api64.dll</c> 把宿主进程崩掉（0xC0000005 之类）之后，
/// 本次运行不再用它，后续成就读取直接跳过。
/// <para>
/// 只存路径、不写盘：重启后重新试一次是合理的（玩家可能修好了 DLL），
/// 而同一进程里反复拿同一个文件去崩是纯粹的浪费 —— 每次崩都会留下几千毫秒和一条崩溃记录。
/// </para>
/// </summary>
public static class SteamApiCrashBlacklist
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Crashed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>这个路径是否已经崩过。</summary>
    /// <param name="path">DLL 路径。</param>
    public static bool IsCrashed(string path)
    {
        lock (Gate)
        {
            return Crashed.Contains(path);
        }
    }

    /// <summary>把路径拉黑；已在名单里时返回 false。</summary>
    /// <param name="path">DLL 路径。</param>
    public static bool MarkCrashed(string path)
    {
        lock (Gate)
        {
            return Crashed.Add(path);
        }
    }

    /// <summary>清空名单（自检用；正常运行期不该调用）。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Crashed.Clear();
        }
    }
}
