namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;

using PdxLauncher.Core;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 安装脚本自检。
/// installer/PdxNext.iss 不参与编译：写错一个指令不会让任何代码报错，只会让打出来的安装程序
/// 行为悄悄变掉（Inno 的 DisableDirPage 默认是 auto，检测到旧安装时会把「选择目标位置」整页隐藏），
/// 更新器拼出来的静默参数同理。这里把这几条约定钉死。
/// </summary>
public static class InstallerScriptChecks
{
    /// <summary>
    /// 安装包标识；改掉它会让新旧版本变成两个互不认识的程序（升级链断掉）。
    /// Inno 里字面量的大括号要转义成 {{，所以这里在 PdxIdentity.AppId 前补一个 {。
    /// </summary>
    private const string ExpectedAppId = "{" + PdxIdentity.AppId;

    /// <summary>升级时必须保留的运行时状态，不能被安装包覆盖。</summary>
    private static readonly string[] RequiredExcludes =
    [
        @"data\*",
        "data-root.txt",
        "*.log",
        @"update-cache\*",
        "install-mode.txt",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var issPath = Path.Combine(root, "installer", "PdxNext.iss");
        if (!ChecksContext.Check("installer/PdxNext.iss 存在", fs.FileExists(issPath), issPath))
        {
            return ChecksContext.Failures - before;
        }

        // 这个文件在仓库里一直带 UTF-8 BOM（ISCC 7 也能自动识别无 BOM 的 UTF-8，但带 BOM 时
        // 编辑器/脚本按 UTF-8 读不会有歧义，避免被另存成 GBK 后 [CustomMessages] 中文变乱码）。
        var raw = fs.ReadAllBytes(issPath);
        var hasBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        ChecksContext.Check("installer/PdxNext.iss 保持 UTF-8 BOM", hasBom, null);

        var text = fs.ReadAllText(issPath).TrimStart('\uFEFF');
        var sections = ParseSections(text);

        CheckSetupSection(sections);
        CheckFilesSection(sections);
        CheckCustomMessages(sections);
        CheckCodeSection(sections);
        CheckUpdaterArgs(fs, root);
        CheckVerifyScript(fs, root);

        return ChecksContext.Failures - before;
    }

    private static void CheckVerifyScript(IFileSystem fs, string root)
    {
        Console.WriteLine("[InstallerScriptChecks] 安装版本校验脚本能读到自定义安装目录");
        var path = Path.Combine(root, "tools", "verify-installed-version.ps1");
        if (!ChecksContext.Check("verify-installed-version.ps1 存在", fs.FileExists(path), path))
        {
            return;
        }

        // Windows PowerShell 5.1 读无 BOM 的 .ps1 会按 ANSI 解码，中文注释行会把下一行吞进注释，
        // 结果脚本静默少跑一段代码（曾经导致卸载项里的安装路径读不到，脚本永远报“未安装”）。
        var raw = fs.ReadAllBytes(path);
        var hasBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        ChecksContext.Check("verify-installed-version.ps1 带 UTF-8 BOM（否则 PowerShell 5.1 按 ANSI 读）", hasBom, null);

        var text = fs.ReadAllText(path).TrimStart('\uFEFF');
        ChecksContext.Check(
            "verify-installed-version.ps1 读取卸载项里的实际安装路径",
            text.Contains("Inno Setup: App Path", StringComparison.Ordinal)
            && text.Contains("'HKCU:'", StringComparison.Ordinal)
            && text.Contains("'HKLM:'", StringComparison.Ordinal),
            null);
        ChecksContext.Check(
            "verify-installed-version.ps1 的卸载键与 PdxIdentity.UninstallKeyName 一致",
            text.Contains(PdxIdentity.UninstallKeyName, StringComparison.Ordinal),
            null);
    }

    private static void CheckSetupSection(Dictionary<string, List<string>> sections)
    {
        Console.WriteLine("[InstallerScriptChecks] [Setup] 关键指令");
        if (!sections.TryGetValue("Setup", out var setup))
        {
            ChecksContext.Check("[Setup] 段存在", false, "缺少 [Setup]");
            return;
        }

        // auto（Inno 默认值）会在发现旧安装目录时隐藏整个目录页，用户就再也选不了安装位置。
        CheckDirective(setup, "DisableDirPage", "no", "目录页必须始终显示");
        // 显式 no 之后，升级时靠它把上次的目录预填进目录页。
        CheckDirective(setup, "UsePreviousAppDir", "yes", "升级时预填上次的安装目录");
        CheckDirective(setup, "DefaultDirName", @"{autopf}\PdxNext", "全新安装的默认目录");
        CheckDirective(setup, "PrivilegesRequired", "lowest", "默认按用户安装，不弹 UAC");
        CheckDirective(
            setup,
            "PrivilegesRequiredOverridesAllowed",
            "commandline dialog",
            "更新器用 /CURRENTUSER 或 /ALLUSERS 指定安装模式");

        var appId = GetDirective(setup, "AppId");
        ChecksContext.Check("[Setup] AppId 未改动", ExpectedAppId == appId, appId);
    }

    private static void CheckFilesSection(Dictionary<string, List<string>> sections)
    {
        Console.WriteLine("[InstallerScriptChecks] [Files] 升级不能覆盖运行时数据");
        if (!sections.TryGetValue("Files", out var files))
        {
            ChecksContext.Check("[Files] 段存在", false, "缺少 [Files]");
            return;
        }

        var source = files
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("Source:", StringComparison.OrdinalIgnoreCase));
        if (!ChecksContext.Check("[Files] 有 Source 行", source is not null, source))
        {
            return;
        }

        var match = Regex.Match(source!, "Excludes:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant);
        if (!ChecksContext.Check("[Files] Source 带 Excludes", match.Success, source))
        {
            return;
        }

        var excludes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredExcludes)
        {
            ChecksContext.Check("[Files] 排除 " + required, excludes.Contains(required), match.Groups[1].Value);
        }
    }

    private static void CheckCustomMessages(Dictionary<string, List<string>> sections)
    {
        Console.WriteLine("[InstallerScriptChecks] [CustomMessages] 中英文成对");
        if (!sections.TryGetValue("CustomMessages", out var messages))
        {
            ChecksContext.Check("[CustomMessages] 段存在", false, "缺少 [CustomMessages]");
            return;
        }

        var keys = messages
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith(';'))
            .Select(line => line.Split('=', 2)[0].Trim())
            .Where(key => key.Length > 0)
            .ToList();

        var zh = keys.Where(key => key.StartsWith("zhcn.", StringComparison.OrdinalIgnoreCase)).ToList();
        var en = keys.Where(key => key.StartsWith("en.", StringComparison.OrdinalIgnoreCase)).ToList();

        ChecksContext.Check("[CustomMessages] 有中文条目", zh.Count > 0, string.Join(", ", zh));
        foreach (var key in zh)
        {
            var counterpart = "en." + key["zhcn.".Length..];
            ChecksContext.Check("[CustomMessages] 有对应英文 " + counterpart, en.Contains(counterpart, StringComparer.OrdinalIgnoreCase));
        }

        foreach (var key in en)
        {
            var counterpart = "zhcn." + key["en.".Length..];
            ChecksContext.Check("[CustomMessages] 有对应中文 " + counterpart, zh.Contains(counterpart, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void CheckCodeSection(Dictionary<string, List<string>> sections)
    {
        Console.WriteLine("[InstallerScriptChecks] [Code] 目录页校验与安装模式标记");
        if (!sections.TryGetValue("Code", out var codeLines))
        {
            ChecksContext.Check("[Code] 段存在", false, "缺少 [Code]");
            return;
        }

        var code = string.Join('\n', codeLines);

        // 卸载键与程序文件名必须与运行时判定共用同一份常量，否则改 GUID/改名时两边会悄悄分家。
        ChecksContext.Check(
            "[Code] UninstallKey 与 PdxIdentity.UninstallKeyName 一致",
            code.Contains(PdxIdentity.UninstallKeyName, StringComparison.Ordinal),
            null);
        ChecksContext.Check(
            "[Code] 程序文件名与 PdxIdentity.AppExecutableName 一致",
            code.Contains("AppExeName = '" + PdxIdentity.AppExecutableName + "'", StringComparison.Ordinal),
            null);

        // 安装模式标记决定数据目录落在哪里（见 PdxDataRoot.IsPerMachineInstall）。
        ChecksContext.Check(
            "[Code] 仍写入 install-mode.txt",
            code.Contains("install-mode.txt", StringComparison.OrdinalIgnoreCase),
            null);

        var handlerIndex = code.IndexOf("function NextButtonClick(", StringComparison.Ordinal);
        if (!ChecksContext.Check("[Code] 有 NextButtonClick 目录页校验", handlerIndex >= 0, null))
        {
            return;
        }

        var handler = code[handlerIndex..];
        ChecksContext.Check(
            "[Code] 目录页校验只处理 wpSelectDir",
            handler.Contains("wpSelectDir", StringComparison.Ordinal),
            null);

        // 静默安装同样会走 NextButtonClick，返回 False 会让 Setup 直接退出；
        // 自动更新必须无条件放行，所以 WizardSilent 的提前返回必须在任何 MsgBox 之前。
        var silentIndex = handler.IndexOf("WizardSilent", StringComparison.Ordinal);
        var messageIndex = handler.IndexOf("MsgBox(", StringComparison.Ordinal);
        ChecksContext.Check(
            "[Code] 静默安装先放行再校验",
            silentIndex >= 0 && (messageIndex < 0 || silentIndex < messageIndex),
            "WizardSilent@" + silentIndex + "，MsgBox@" + messageIndex);

        CheckConstants(code);
    }

    /// <summary>
    /// 扫描 ExpandConstant 里的常量名。写错常量名（例如 {userprofile}）编译期不报错，
    /// 但运行到那行会弹「内部错误: Unknown constant」，目录页直接卡死，只能靠清单兜住。
    /// </summary>
    private static void CheckConstants(string code)
    {
        string[] known =
        [
            "app", "win", "sys", "sysnative", "syswow64", "src", "sd", "tmp", "backup", "group", "groupname",
            "commonpf", "commonpf32", "commonpf64", "commonappdata", "commoncf", "commoncf32", "commoncf64",
            "commondesktop", "commonprograms", "commonstartmenu", "commonstartup", "commontemplates",
            "commonfonts", "commondocs", "userpf", "localappdata", "userappdata", "userdocs", "usercf",
            "userdesktop", "userprograms", "userstartmenu", "userstartup", "usertemplates", "userfavorites",
            "usertemp", "userfonts",
        ];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(code, "ExpandConstant\\('\\{([^'}]+)\\}", RegexOptions.CultureInvariant))
        {
            var name = match.Groups[1].Value;
            if (name.StartsWith('%') || name.StartsWith("cm:", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith('#'))
            {
                continue;
            }

            seen.Add(name);
        }

        foreach (var name in seen)
        {
            ChecksContext.Check(
                "[Code] ExpandConstant 常量存在：" + name,
                known.Contains(name, StringComparer.OrdinalIgnoreCase),
                "未知常量会在运行时抛 Unknown constant");
        }
    }

    private static void CheckUpdaterArgs(IFileSystem fs, string root)
    {
        Console.WriteLine("[InstallerScriptChecks] 自动更新仍走静默安装");
        var path = Path.Combine(root, "src", "PdxLauncher.Core", "Updates", "UpdateService.cs");
        if (!ChecksContext.Check("UpdateService.cs 存在", fs.FileExists(path), path))
        {
            return;
        }

        var text = fs.ReadAllText(path);
        var match = Regex.Match(text, "= \"(/[A-Z]+)[^\"]*\"", RegexOptions.CultureInvariant);
        ChecksContext.Check(
            "更新器安装参数以 /SILENT 开头",
            match.Success && match.Groups[1].Value == "/SILENT",
            match.Success ? match.Groups[1].Value : "未找到安装参数");
    }

    private static void CheckDirective(List<string> lines, string key, string expected, string why)
    {
        var value = GetDirective(lines, key);
        ChecksContext.Check(
            "[Setup] " + key + " = " + expected + "（" + why + "）",
            string.Equals(value, expected, StringComparison.OrdinalIgnoreCase),
            value ?? "缺少该指令");
    }

    private static string? GetDirective(List<string> lines, string key)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            var parts = line.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1].Trim();
            }
        }

        return null;
    }

    /// <summary>按 [Section] 切块；节内保留原始行（含注释），供各处按需解析。</summary>
    private static Dictionary<string, List<string>> ParseSections(string text)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
            {
                current = [];
                sections[trimmed[1..^1].Trim()] = current;
                continue;
            }

            current?.Add(line);
        }

        return sections;
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
