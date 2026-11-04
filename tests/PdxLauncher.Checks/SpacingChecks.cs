namespace PdxLauncher.Checks;

using System.Globalization;
using System.Text.RegularExpressions;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 4px 间距网格自检。规则来自 WinUI Gallery 的「Spacing」页：
/// <c>A best practice in design is to use a 4px grid. This means that any spacing or sizing should be a
/// multiple of 4.</c>（epx = DIP），常用值是 4 / 8 / 12 / 16 / 24 / 36 / 48，但硬规则只有「4 的倍数」。
/// <para>
/// 覆盖范围：XAML 里的 <c>Padding</c> / <c>Margin</c> / <c>Spacing</c> / <c>RowSpacing</c> /
/// <c>ColumnSpacing</c>（含 <c>Setter Property="Padding|Margin"</c> 的 Value），以及 C# 里
/// <c>Spacing = 数字</c> 与全数字字面量的 <c>new Thickness(...)</c>；<c>CornerRadius</c> 只允许 0 / 4 / 8。
/// </para>
/// <para>
/// 不在检查范围内（有意）：<c>BorderThickness</c>（线宽，不是间距）、含表达式 / 三元 / 标识符的
/// <c>new Thickness(...)</c>（如 <c>new Thickness(16 + (item.Level * 20), 0, 0, 0)</c>），
/// 以及 Width / Height（控件尺寸不套用间距规则）。新增这类写法时要人工确认，见 docs/UI-SPACING.md。
/// </para>
/// </summary>
public static class SpacingChecks
{
    private static readonly Regex CommentPattern = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AttributePattern = new(
        @"\b(Padding|Margin|Spacing|RowSpacing|ColumnSpacing)=""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex CornerRadiusPattern = new(@"\bCornerRadius=""([0-9.,]+)""", RegexOptions.Compiled);
    private static readonly Regex SetterPattern = new(@"<Setter\b[^>]*>", RegexOptions.Compiled);
    private static readonly Regex SetterPropertyPattern = new(@"Property=""(Padding|Margin)""", RegexOptions.Compiled);
    private static readonly Regex SetterValuePattern = new(@"Value=""([^""]*)""", RegexOptions.Compiled);

    private static readonly Regex CsSpacingPattern = new(
        @"\b(Spacing|ColumnSpacing|RowSpacing)\s*=\s*([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled);

    private static readonly Regex ThicknessPattern = new(@"new Thickness\(([^()]*)\)", RegexOptions.Compiled);
    private static readonly Regex CornerRadiusCtorPattern = new(@"new CornerRadius\(([^()]*)\)", RegexOptions.Compiled);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();
        var appRoot = Path.Combine(root, "src", "PdxLauncher.App");

        CheckSelfTest();
        CheckXaml(appRoot);
        CheckCSharp(appRoot);
        CheckDocumentation(root);
        return ChecksContext.Failures - before;
    }

    /// <summary>检查器自己不能把合法写法当违规，否则运行一次就会冤枉全仓。</summary>
    private static void CheckSelfTest()
    {
        ChecksContext.Equal("间距检查器：6,1 判为违规", 1, CountXamlViolations("<Grid Padding=\"6,1\" />"));
        ChecksContext.Equal(
            "间距检查器：ThemeResource 不参与判断",
            0,
            CountXamlViolations("<Border Padding=\"{ThemeResource DefaultPadding}\" />"));
        ChecksContext.Equal("间距检查器：8,4 合法", 0, CountXamlViolations("<Grid Padding=\"8,4\" />"));
        ChecksContext.Equal("间距检查器：0 合法", 0, CountXamlViolations("<Grid Margin=\"0\" />"));
        ChecksContext.Equal(
            "间距检查器：Setter 的 Padding 也检查",
            1,
            CountXamlViolations("<Setter Property=\"Padding\" Value=\"10,6\" />"));
    }

    private static void CheckXaml(string appRoot)
    {
        var violations = 0;
        foreach (var path in EnumerateSourceFiles(appRoot, "*.xaml"))
        {
            var relative = RelativePath(appRoot, path);
            var text = File.ReadAllText(path);
            var found = FindXamlViolations(text);
            violations += found.Count;
            ChecksContext.Check(
                "间距 XAML：" + relative + " 全部落在 4px 网格上",
                found.Count == 0,
                found.Count == 0 ? null : string.Join("；", found.Take(6)));
        }

        ChecksContext.Equal("间距 XAML：非 4 倍数总数为 0", 0, violations);
    }

    private static void CheckCSharp(string appRoot)
    {
        var violations = 0;
        foreach (var path in EnumerateSourceFiles(appRoot, "*.cs"))
        {
            var relative = RelativePath(appRoot, path);
            var found = FindCSharpViolations(File.ReadAllText(path));
            violations += found.Count;
            ChecksContext.Check(
                "间距 C#：" + relative + " 全部落在 4px 网格上",
                found.Count == 0,
                found.Count == 0 ? null : string.Join("；", found.Take(6)));
        }

        ChecksContext.Equal("间距 C#：非 4 倍数总数为 0", 0, violations);
    }

    private static void CheckDocumentation(string root)
    {
        var path = Path.Combine(root, "docs", "UI-SPACING.md");
        var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        ChecksContext.Check("间距文档：docs/UI-SPACING.md 存在", text.Length > 0);
        ChecksContext.Check(
            "间距文档：写明了官方依据（content-basics）",
            text.Contains("content-basics", StringComparison.Ordinal));
        ChecksContext.Check(
            "间距文档：写明了常用值表（含 4epx / 36epx）",
            text.Contains("4epx", StringComparison.Ordinal) && text.Contains("36epx", StringComparison.Ordinal));
    }

    private static List<string> FindXamlViolations(string text)
    {
        var result = new List<string>();
        var stripped = StripComments(text);

        foreach (Match match in AttributePattern.Matches(stripped))
        {
            var value = match.Groups[2].Value;
            if (!TryParseNumbers(value, out var numbers))
            {
                continue;
            }

            if (numbers.Any(number => number % 4 != 0))
            {
                result.Add($"第 {LineOf(stripped, match.Index)} 行 {match.Groups[1].Value}=\"{value}\"");
            }
        }

        foreach (Match setter in SetterPattern.Matches(stripped))
        {
            if (!SetterPropertyPattern.IsMatch(setter.Value))
            {
                continue;
            }

            var valueMatch = SetterValuePattern.Match(setter.Value);
            if (!valueMatch.Success || !TryParseNumbers(valueMatch.Groups[1].Value, out var numbers))
            {
                continue;
            }

            if (numbers.Any(number => number % 4 != 0))
            {
                result.Add($"第 {LineOf(stripped, setter.Index)} 行 Setter={valueMatch.Groups[1].Value}");
            }
        }

        foreach (Match match in CornerRadiusPattern.Matches(stripped))
        {
            var value = match.Groups[1].Value;
            if (!TryParseNumbers(value, out var radii))
            {
                continue;
            }

            if (radii.Any(radius => radius != 0 && radius != 4 && radius != 8))
            {
                result.Add($"第 {LineOf(stripped, match.Index)} 行 CornerRadius=\"{value}\"（只允许 0/4/8）");
            }
        }

        return result;
    }

    private static List<string> FindCSharpViolations(string text)
    {
        var result = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // 边框线宽不是间距：3px 的引用块竖线是故意的，别让检查器把它当违规。
            if (line.Contains("BorderThickness", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in CsSpacingPattern.Matches(line))
            {
                if (double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    && value % 4 != 0)
                {
                    result.Add($"第 {i + 1} 行 {match.Groups[1].Value} = {match.Groups[2].Value}");
                }
            }

            foreach (Match match in ThicknessPattern.Matches(line))
            {
                if (!TryParseNumbers(match.Groups[1].Value, out var numbers))
                {
                    continue;
                }

                if (numbers.Any(number => number % 4 != 0))
                {
                    result.Add($"第 {i + 1} 行 new Thickness({match.Groups[1].Value})");
                }
            }

            foreach (Match match in CornerRadiusCtorPattern.Matches(line))
            {
                if (!TryParseNumbers(match.Groups[1].Value, out var radii))
                {
                    continue;
                }

                if (radii.Any(radius => radius != 0 && radius != 4 && radius != 8))
                {
                    result.Add($"第 {i + 1} 行 new CornerRadius({match.Groups[1].Value})（只允许 0/4/8）");
                }
            }
        }

        return result;
    }

    private static int CountXamlViolations(string xaml) => FindXamlViolations(xaml).Count;

    /// <summary>把 {@code <!-- -->} 换成等长空格，保留换行，行号才对得上。</summary>
    private static string StripComments(string text)
        => CommentPattern.Replace(text, match => Regex.Replace(match.Value, "[^\n]", " "));

    /// <summary>把 "8,4" / "0" / "{ThemeResource X}" 解析成数字列表；含非数字（资源、绑定）时返回 false。</summary>
    private static bool TryParseNumbers(string value, out double[] numbers)
    {
        numbers = Array.Empty<double>();
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Contains('{', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = trimmed.Split(',');
        var parsed = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
            {
                return false;
            }
        }

        numbers = parsed;
        return true;
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string appRoot, string pattern)
        => Directory
            .EnumerateFiles(appRoot, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static string RelativePath(string appRoot, string path)
    {
        var relative = Path.GetRelativePath(appRoot, path);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
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

        return Directory.GetCurrentDirectory();
    }
}
