namespace PdxLauncher.Checks;

using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 文案换行自检：<c>tools/i18n/**/*.json</c> 里必须是 JSON 的 <c>\n</c> 转义，
/// 不能出现字面反斜杠 + n（键盘上敲的「\\n」）。
/// <para>
/// 真机踩过一次：三个对话框文案写成了 <c>\\n</c>，JSON 解析后得到的是反斜杠 + n
/// 两个普通字符，生成器再把它转义进 <c>Strings.g.cs</c>，对话框里就原样显示「\n\n」。
/// 这类错误编译、生成、旧自检都能过，只能靠扫描源文本钉住。
/// </para>
/// </summary>
public static class I18nChecks
{
    /// <summary>匹配两个反斜杠 + n：JSON 里写成 <c>\\n</c> 的字面转义（不是换行）。</summary>
    private static readonly Regex LiteralBackslashN = new(@"\\\\n", RegexOptions.CultureInvariant);

    /// <summary>三个必须含真实换行的对话框键。</summary>
    private static readonly string[] MultiLineDialogKeys =
    [
        "settings.appData.dialogClearContent",
        "settings.cache.dialogClearAllContent",
        "settings.danger.dialogContent",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        // 检测器自身的变异守卫：字面 \\n 必须命中，真实 \n 转义不能命中。
        ChecksContext.Check(
            "i18n 换行: 检测器识别字面 \\\\n",
            LiteralBackslashN.IsMatch("前文\\\\n后文"));
        ChecksContext.Check(
            "i18n 换行: 检测器不误报 JSON 换行转义",
            !LiteralBackslashN.IsMatch("前文\\n后文"));

        var root = FindWorkspaceRoot();
        var sourceRoot = Path.Combine(root, "tools", "i18n");
        var files = Directory.EnumerateFiles(sourceRoot, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        ChecksContext.Check("i18n 换行: 找到两种语言的文案源", files.Count >= 2, "共 " + files.Count + " 个 JSON");

        var textValues = 0;
        var valuesWithNewline = 0;
        var literalHits = new List<string>();
        var perLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var path in files)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var text = File.ReadAllText(path);
            var hits = LiteralBackslashN.Matches(text);
            foreach (Match hit in hits)
            {
                literalHits.Add(relative + " 第 " + (text.AsSpan(0, hit.Index).Count('\n') + 1) + " 行");
            }

            ChecksContext.Check(
                "i18n 换行: " + relative + " 没有字面 \\\\n",
                hits.Count == 0,
                hits.Count == 0 ? null : hits.Count + " 处");

            using var document = JsonDocument.Parse(text);
            var language = Path.GetFileName(Path.GetDirectoryName(path))!;
            if (!perLanguage.TryGetValue(language, out var table))
            {
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                perLanguage[language] = table;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value.GetString() ?? string.Empty;
                table[property.Name] = value;
                textValues++;
                if (value.Contains('\n'))
                {
                    valuesWithNewline++;
                }
            }
        }

        ChecksContext.Check(
            "i18n 换行: 文案里确实存在多行文本",
            valuesWithNewline >= MultiLineDialogKeys.Length * 2,
            valuesWithNewline + " / " + textValues + " 条含换行");
        ChecksContext.Check("i18n 换行: 六处字面转义已清零", literalHits.Count == 0, string.Join("；", literalHits));

        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var readable = perLanguage.TryGetValue(language, out var table) && table.Count > 0;
            ChecksContext.Check("i18n 换行: " + language + " 文案源可读", readable);

            if (!readable || table is null)
            {
                continue;
            }

            foreach (var key in MultiLineDialogKeys)
            {
                if (!table.TryGetValue(key, out var value) || value is null)
                {
                    ChecksContext.Check("i18n 换行: " + language + " " + key + " 含真实换行", false, "缺键");
                    continue;
                }

                ChecksContext.Check(
                    "i18n 换行: " + language + " " + key + " 含真实换行",
                    value.Contains('\n'),
                    "长度 " + value.Length);
            }
        }

        // 生成物也要钉住：真实换行在 C# 源码里是单个 \\n；两个反斜杠就是又把字面量写回去了。
        var generatedPath = Path.Combine(root, "src", "PdxLauncher.App", "Localization", "Strings.g.cs");
        ChecksContext.Check("i18n 换行: 生成物存在", File.Exists(generatedPath), generatedPath);
        if (File.Exists(generatedPath))
        {
            var generated = File.ReadAllText(generatedPath);
            ChecksContext.Check(
                "i18n 换行: 生成物里没有字面 \\\\n",
                !LiteralBackslashN.IsMatch(generated));

            var generatedLines = File.ReadAllLines(generatedPath);
            foreach (var key in MultiLineDialogKeys)
            {
                var entries = generatedLines
                    .Where(line => line.Contains("[\"" + key + "\"] = ", StringComparison.Ordinal))
                    .ToList();
                ChecksContext.Check(
                    "i18n 换行: 生成物 " + key + " 两种语言各一条",
                    entries.Count == 2,
                    "实际 " + entries.Count + " 条");
                ChecksContext.Check(
                    "i18n 换行: 生成物 " + key + " 用真实换行转义",
                    entries.Count == 2
                        && entries.All(line => line.Contains("\\n", StringComparison.Ordinal)
                            && !line.Contains("\\\\n", StringComparison.Ordinal)));
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "i18n")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("从 " + AppContext.BaseDirectory + " 向上找不到 tools/i18n");
    }
}


