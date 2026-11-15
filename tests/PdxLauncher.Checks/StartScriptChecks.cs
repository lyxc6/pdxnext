namespace PdxLauncher.Checks;

using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 一键启动入口自检。
/// start.cmd 和 .vscode/tasks.json 都是「不参与编译、错了也不会让程序报错」的接线点：
/// 少一个提示键只会静默少打一行，exe 路径写错只会在运行时才「找不到可执行文件」，
/// tasks.json 里的引号写坏只会在 VS Code 里报解析错误。这里把它们一起钉死。
/// </summary>
public static class StartScriptChecks
{
    /// <summary>exe 路径里出现的 TFM 片段（如 net10.0-windows10.0.26100.0）。</summary>
    private static readonly Regex TfmPattern = new(@"net\d+\.\d+-windows\d+(?:\.\d+)+", RegexOptions.CultureInvariant);

    /// <summary>顶层 .cmd 里对 messages.txt 的取键调用。</summary>
    private static readonly Regex MsgKeyPattern = new(@"scripts\\msg\.cmd""\s+([A-Za-z0-9_]+)", RegexOptions.CultureInvariant);

    /// <summary>所有参与启动/推进的顶层 .cmd：它们都必须纯 ASCII 且只调用 messages.txt 里已有的键。</summary>
    private static readonly string[] TopLevelScripts = ["start.cmd", "dev.cmd", "status.cmd"];

    /// <summary>按进程名结束/统计 PdxLauncher.App 的旧写法（会误伤别的安装目录里的副本）。</summary>
    private static readonly Regex BareProcessNamePattern =
        new(
            @"taskkill\s+/im\s+PdxLauncher\.App\.exe|imagename eq PdxLauncher\.App\.exe|Get-Process\s+(?:-Name\s+)?PdxLauncher\.App",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var scriptPath = Path.Combine(root, "start.cmd");
        var messagesPath = Path.Combine(root, "scripts", "messages.txt");

        if (!ChecksContext.Check("start.cmd 存在", fs.FileExists(scriptPath), scriptPath))
        {
            return ChecksContext.Failures - before;
        }

        var bytes = fs.ReadAllBytes(scriptPath);
        CheckAsciiOnly(bytes);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        CheckMessageKeys(fs, text, messagesPath);
        CheckExecutablePaths(fs, root, text);
        CheckSerialBuild(text);
        CheckVsCodeTasks(fs, root);
        CheckTopLevelScripts(fs, root, messagesPath);

        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 三个顶层 .cmd 的共同契约：纯 ASCII、提示键都在 messages.txt 里、构建必须 -m:1、
    /// 并且不能按「进程名」结束/统计 PdxLauncher.App.exe。
    /// <para>
    /// 最后一条是这次多盘并存的根因：同一台机器上装在别的盘的副本同名，
    /// 按名字杀会顺手关掉用户正在用的那份，按名字统计会把别人的 PID 报成「自己启动成功」。
    /// </para>
    /// </summary>
    private static void CheckTopLevelScripts(IFileSystem fs, string root, string messagesPath)
    {
        Console.WriteLine("[StartScriptChecks] dev.cmd / status.cmd 同一套契约");
        foreach (var name in TopLevelScripts)
        {
            var path = Path.Combine(root, name);
            if (!ChecksContext.Check(name + " 存在", fs.FileExists(path), path))
            {
                continue;
            }

            var bytes = fs.ReadAllBytes(path);
            var offender = Array.FindIndex(bytes, b => b >= 0x80);
            ChecksContext.Check(
                name + " 全部字节 < 0x80",
                offender < 0,
                offender < 0 ? bytes.Length + "B" : "第 " + (1 + CountNewlines(bytes, offender)) + " 行有非 ASCII 字节 0x" + bytes[offender].ToString("X2"));

            var text = System.Text.Encoding.UTF8.GetString(bytes);
            CheckMessageKeys(fs, text, messagesPath, name);

            var buildLine = text.Split('\n').FirstOrDefault(l => l.Contains("dotnet build", StringComparison.Ordinal));
            if (buildLine is not null)
            {
                ChecksContext.Check(name + " 的 dotnet build 带 -m:1", buildLine.Contains("-m:1", StringComparison.Ordinal), buildLine.Trim());
            }

            var bare = BareProcessNamePattern.Match(text);
            ChecksContext.Check(
                name + " 不按进程名结束/统计 PdxLauncher.App.exe（多盘副本会被误伤）",
                !bare.Success,
                bare.Success ? bare.Value : "ok");

            if (name is "dev.cmd" or "status.cmd" or "start.cmd")
            {
                ChecksContext.Check(
                    name + " 走 scripts\\pdx-app-process.ps1 按 exe 路径过滤",
                    text.Contains(@"scripts\pdx-app-process.ps1", StringComparison.Ordinal),
                    name);
            }
        }

        var helper = Path.Combine(root, "scripts", "pdx-app-process.ps1");
        ChecksContext.Check("scripts\\pdx-app-process.ps1 存在", fs.FileExists(helper), helper);
        if (fs.FileExists(helper))
        {
            var helperText = fs.ReadAllText(helper);
            ChecksContext.Check(
                "pdx-app-process.ps1 支持 status/stop/wait 三种动作",
                helperText.Contains("'status'", StringComparison.Ordinal)
                    && helperText.Contains("'stop'", StringComparison.Ordinal)
                    && helperText.Contains("'wait'", StringComparison.Ordinal));
            ChecksContext.Check(
                "pdx-app-process.ps1 按 Path 全等比较（不按进程名杀）",
                helperText.Contains("OrdinalIgnoreCase", StringComparison.Ordinal)
                    && helperText.Contains("LOCAL_PIDS", StringComparison.Ordinal)
                    && helperText.Contains("OTHER_PIDS", StringComparison.Ordinal));
        }
    }

    private static int CountNewlines(byte[] bytes, int upto)
    {
        var lines = 1;
        for (var i = 0; i < upto; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static void CheckAsciiOnly(byte[] bytes)
    {
        Console.WriteLine("[StartScriptChecks] cmd 必须纯 ASCII");
        var offender = Array.FindIndex(bytes, b => b >= 0x80);
        if (offender < 0)
        {
            ChecksContext.Check("start.cmd 全部字节 < 0x80", true, bytes.Length + "B");
            return;
        }

        var line = 1;
        for (var i = 0; i < offender; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                line++;
            }
        }

        ChecksContext.Check("start.cmd 全部字节 < 0x80", false, "第 " + line + " 行有非 ASCII 字节 0x" + bytes[offender].ToString("X2"));
    }

    private static void CheckMessageKeys(IFileSystem fs, string text, string messagesPath, string scriptName = "start.cmd")
    {
        Console.WriteLine("[StartScriptChecks] 提示键都在 messages.txt 里（" + scriptName + "）");
        if (!ChecksContext.Check("scripts\\messages.txt 存在", fs.FileExists(messagesPath), messagesPath))
        {
            return;
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in fs.ReadAllText(messagesPath).Split('\n'))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (colon > 0)
            {
                known.Add(line[..colon].Trim());
            }
        }

        var keys = MsgKeyPattern.Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.Ordinal).ToList();
        ChecksContext.Check(scriptName + " 至少调用一次 msg.cmd", keys.Count > 0, keys.Count + " 个键");
        foreach (var key in keys)
        {
            ChecksContext.Check(scriptName + " 提示键 " + key + " 已定义", known.Contains(key));
        }
    }

    private static void CheckExecutablePaths(IFileSystem fs, string root, string text)
    {
        Console.WriteLine("[StartScriptChecks] exe 路径与 TargetFramework 一致");
        var csprojPath = Path.Combine(root, "src", "PdxLauncher.App", "PdxLauncher.App.csproj");
        if (!ChecksContext.Check("PdxLauncher.App.csproj 存在", fs.FileExists(csprojPath), csprojPath))
        {
            return;
        }

        var csproj = fs.ReadAllText(csprojPath);
        var match = Regex.Match(csproj, @"<TargetFramework>([^<]+)</TargetFramework>", RegexOptions.CultureInvariant);
        if (!ChecksContext.Check("csproj 里有 TargetFramework", match.Success))
        {
            return;
        }

        var tfm = match.Groups[1].Value.Trim();
        var found = TfmPattern.Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
        ChecksContext.Check("start.cmd 里写了两条（Release/Debug）exe 路径", found.Count == 1, string.Join(", ", found));
        ChecksContext.Equal("start.cmd 的 TFM 与 csproj 一致", tfm, found.FirstOrDefault() ?? string.Empty);

        foreach (var config in new[] { "Release", "Debug" })
        {
            var expected = "bin\\x64\\" + config + "\\" + tfm + "\\win-x64\\PdxLauncher.App.exe";
            ChecksContext.Check("start.cmd 含 " + config + " exe 路径", text.Contains(expected, StringComparison.Ordinal), expected);
        }
    }

    private static void CheckSerialBuild(string text)
    {
        Console.WriteLine("[StartScriptChecks] 构建必须是 -m:1");
        var line = text.Split('\n').FirstOrDefault(l => l.Contains("dotnet build", StringComparison.Ordinal));
        ChecksContext.Check("start.cmd 里有一行 dotnet build", line is not null);
        ChecksContext.Check("dotnet build 带 -m:1（并行 MSBuild 会静默丢 x64 平台）", line is not null && line.Contains("-m:1", StringComparison.Ordinal));
    }

    private static void CheckVsCodeTasks(IFileSystem fs, string root)
    {
        Console.WriteLine("[StartScriptChecks] .vscode/tasks.json");
        var tasksPath = Path.Combine(root, ".vscode", "tasks.json");
        if (!ChecksContext.Check(".vscode/tasks.json 存在", fs.FileExists(tasksPath), tasksPath))
        {
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(fs.ReadAllText(tasksPath));
        }
        catch (JsonException ex)
        {
            ChecksContext.Check(".vscode/tasks.json 能被解析", false, ex.Message);
            return;
        }

        using (doc)
        {
            ChecksContext.Check(".vscode/tasks.json 能被解析", true);
            var rootEl = doc.RootElement;
            ChecksContext.Equal("version 为 2.0.0", "2.0.0", rootEl.TryGetProperty("version", out var version) ? version.GetString() : null);

            if (!rootEl.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array || tasks.GetArrayLength() == 0)
            {
                ChecksContext.Check("tasks 是非空数组", false);
                return;
            }

            ChecksContext.Check("tasks 是非空数组", true, tasks.GetArrayLength() + " 个任务");

            var labels = new List<string>();
            foreach (var task in tasks.EnumerateArray())
            {
                var label = task.TryGetProperty("label", out var l) ? l.GetString() : null;
                if (!string.IsNullOrEmpty(label))
                {
                    labels.Add(label);
                }
            }

            var duplicates = labels.GroupBy(x => x, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            ChecksContext.Check("任务 label 不重复", duplicates.Count == 0, string.Join(", ", duplicates));

            var first = tasks[0];
            var firstLabel = first.TryGetProperty("label", out var fl) ? fl.GetString() : null;
            ChecksContext.Check("第一个任务是默认一键启动", firstLabel is not null && firstLabel.Contains("一键启动", StringComparison.Ordinal), firstLabel);

            var firstGroup = first.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.Object && g.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True;
            ChecksContext.Check("第一个任务设为默认生成任务", firstGroup);

            var argsText = first.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array
                ? string.Join(" ", args.EnumerateArray().Select(a => a.GetString() ?? string.Empty))
                : string.Empty;
            ChecksContext.Check("默认任务调用 start.cmd", argsText.Contains("start.cmd", StringComparison.Ordinal), argsText);
        }
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
