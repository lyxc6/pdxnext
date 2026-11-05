namespace PdxLauncher.Core.Configuration;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 安装模式判定。判定只看三样东西：调用方给的程序目录、安装器写下的 marker、
/// 卸载项列表。它不读注册表、不读设置，文件系统只做一次「主程序在不在」的存在性检查，
/// 因此同一组输入永远得到同一个结果，可以直接用假的 entry 列表单测。
/// <para>
/// 三条证据同时成立才判已安装：① 某条卸载项的 <c>Inno Setup: App Path</c> 与程序目录
/// 归一化（<see cref="Path.GetFullPath(string)"/>、去尾斜杠、<see cref="StringComparison.OrdinalIgnoreCase"/>）
/// 后相等；② 该条目的 Scope 由它所在的 HKCU/HKLM 决定；③ 程序目录下主程序文件存在。
/// </para>
/// <para>
/// marker 只做佐证：它不能把「无条目」抬成已安装（防止绿色拷贝带走 install-mode.txt），
/// 也不能把「条目路径不匹配」或「exe 已丢」救回来（防止卸载残留被误判）。
/// 它唯一的实际用途是双份条目同时命中时做平票取舍。
/// </para>
/// </summary>
public static class InstallModeResolver
{
    private const string PerMachineMarker = "perMachine";
    private const string PerUserMarker = "perUser";

    /// <summary>
    /// 判定安装模式。不会抛异常：空目录、空列表、非法路径都退化为 <see cref="InstallModeKind.PortableRun"/>。
    /// </summary>
    /// <param name="baseDirectory">程序所在目录（通常是 <c>AppContext.BaseDirectory</c>）。</param>
    /// <param name="installModeMarker"><c>install-mode.txt</c> 的内容；没有时传 null。</param>
    /// <param name="entries">卸载项列表；调用方不应传 null，但这里按空列表容错。</param>
    public static InstallMode Resolve(
        string baseDirectory,
        string? installModeMarker,
        IReadOnlyList<UninstallEntry> entries)
    {
        var candidates = entries ?? [];
        var fullBase = NormalizeDirectory(baseDirectory);
        if (fullBase is null)
        {
            // 连程序目录都无法归一化（空串/非法字符），没有任何可比的证据。
            return InstallMode.PortableRun(InstallModeReason.NoEntry);
        }

        var matched = new List<UninstallEntry>(2);
        foreach (var entry in candidates)
        {
            if (entry is null)
            {
                continue;
            }

            var appPath = NormalizeDirectory(entry.AppPath);
            if (appPath is not null &&
                string.Equals(appPath, fullBase, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(entry);
            }
        }

        if (matched.Count > 0)
        {
            var hit = SelectHit(matched, installModeMarker);
            var exePath = Path.Combine(fullBase, PdxIdentity.AppExecutableName);
            if (!File.Exists(exePath))
            {
                // 键在、路径也对，但程序没了：卸载残留。marker 不能救它。
                return InstallMode.PortableRun(
                    InstallModeReason.ExeMissing,
                    hit.Scope,
                    fullBase,
                    hit.DisplayVersion,
                    hit.RegistryView);
            }

            return InstallMode.Installed(hit.Scope, fullBase, hit.DisplayVersion, hit.RegistryView);
        }

        if (candidates.Count > 0)
        {
            // 有卸载项，但没有一条指向当前目录：绿色拷贝、残留的旧安装或换目录重装。
            return InstallMode.PortableRun(InstallModeReason.PathMismatch);
        }

        return InstallMode.PortableRun(
            HasMarker(installModeMarker) ? InstallModeReason.MarkerOnly : InstallModeReason.NoEntry);
    }

    /// <summary>
    /// 多条条目同时指向同一目录时选一条。现实中 Inno 只会保留一条（双模式并存也被安装器拦截），
    /// 这里只做防御：先按 marker 指定的范围挑，再默认偏向 PerUser（默认安装模式），最后取输入顺序第一条。
    /// </summary>
    private static UninstallEntry SelectHit(IReadOnlyList<UninstallEntry> matches, string? marker)
    {
        var preferred = ScopeFromMarker(marker);
        if (preferred is not null)
        {
            foreach (var entry in matches)
            {
                if (entry.Scope == preferred)
                {
                    return entry;
                }
            }
        }

        foreach (var entry in matches)
        {
            if (entry.Scope == InstallScope.PerUser)
            {
                return entry;
            }
        }

        return matches[0];
    }

    private static InstallScope? ScopeFromMarker(string? marker)
    {
        if (marker is null)
        {
            return null;
        }

        var text = marker.Trim();
        if (string.Equals(text, PerMachineMarker, StringComparison.OrdinalIgnoreCase))
        {
            return InstallScope.PerMachine;
        }

        return string.Equals(text, PerUserMarker, StringComparison.OrdinalIgnoreCase)
            ? InstallScope.PerUser
            : null;
    }

    private static bool HasMarker(string? marker) => !string.IsNullOrWhiteSpace(marker);

    /// <summary>
    /// 目录归一化：展开成绝对路径（顺带消掉 <c>.</c>/<c>..</c> 段），去掉尾部反斜杠，
    /// 根目录（<c>C:\</c>）保持不变。空串或非法路径返回 null。
    /// </summary>
    private static string? NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(directory.Trim());
            return Path.TrimEndingDirectorySeparator(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
