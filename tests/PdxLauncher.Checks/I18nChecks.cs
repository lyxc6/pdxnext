namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 文案换行自检：<c>Strings/&lt;lang&gt;/Resources.resw</c> 里的多行文案必须是真实换行，
/// 不能出现字面反斜杠 + n（键盘上敲的「\n」）。
/// <para>
/// 真机踩过一次：三个对话框文案写成了字面 <c>\n</c>，界面里就原样显示「\n\n」。
/// 这类错误编译与其它自检都能过，只能靠扫描源文本钉住。
/// </para>
/// </summary>
public static class I18nChecks
{
    /// <summary>匹配字面反斜杠 + n（整段文本里的两字符序列）。</summary>
    private static readonly Regex LiteralBackslashN = new(@"\\n", RegexOptions.CultureInvariant);

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

        // 检测器自身的变异守卫：字面反斜杠 + n 必须命中，真实换行不能命中。
        ChecksContext.Check(
            "i18n 换行: 检测器识别字面反斜杠 n",
            LiteralBackslashN.IsMatch("前文" + "\\" + "n后文"));
        ChecksContext.Check(
            "i18n 换行: 检测器不误报真实换行",
            !LiteralBackslashN.IsMatch("前文" + "\n" + "后文"));

        var root = I18nResw.RepoRoot();
        var textValues = 0;
        var valuesWithNewline = 0;
        var literalHits = new List<string>();
        var perLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var language in I18nResw.Languages)
        {
            var path = I18nResw.ResourcesPath(root, language);
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            ChecksContext.Check("i18n 换行: " + relative + " 存在", File.Exists(path), path);
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (Match hit in LiteralBackslashN.Matches(text))
            {
                literalHits.Add(relative + " 第 " + (text.AsSpan(0, hit.Index).Count('\n') + 1) + " 行");
            }

            ChecksContext.Check(
                "i18n 换行: " + relative + " 没有字面反斜杠 n",
                !LiteralBackslashN.IsMatch(text),
                literalHits.Count == 0 ? null : literalHits.Count + " 处");

            var table = I18nResw.Read(root, language);
            perLanguage[language] = table;
            foreach (var value in table.Values)
            {
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
        ChecksContext.Check("i18n 换行: 字面转义已清零", literalHits.Count == 0, string.Join("；", literalHits));

        foreach (var language in I18nResw.Languages)
        {
            var readable = perLanguage.TryGetValue(language, out var table) && table.Count > 0;
            ChecksContext.Check("i18n 换行: " + language + " 文案源可读", readable);
            if (!readable || table is null)
            {
                continue;
            }

            foreach (var key in MultiLineDialogKeys)
            {
                var value = I18nResw.Get(table, key);
                ChecksContext.Check(
                    "i18n 换行: " + language + " " + key + " 含真实换行",
                    value is not null && value.Contains('\n'),
                    value is null ? "缺键" : "长度 " + value.Length);
            }
        }

        // 两种语言的资源名必须完全一致（生成器也会在编译期校验，这里是运行期兜底）。
        var zh = perLanguage.GetValueOrDefault("zh-CN") ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var en = perLanguage.GetValueOrDefault("en-US") ?? new Dictionary<string, string>(StringComparer.Ordinal);
        ChecksContext.Check(
            "i18n 换行: 中英资源名一致",
            zh.Count > 0 && en.Count > 0 && zh.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .SequenceEqual(en.Keys.OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal),
            zh.Count + " / " + en.Count);

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
