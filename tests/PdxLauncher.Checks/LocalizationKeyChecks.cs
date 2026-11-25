namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.CrashAnalysis;
using PdxLauncher.Core.Localization;

/// <summary>
/// 文案键与 <c>Resources.resw</c> 对不上的自检。
/// <para>
/// 真机踩过：崩溃分析页整页文案在界面上显示成 <c>⟨crash.summary.gameDetail⟩</c>。
/// 原因是代码用点分键、而 .resw 的资源名是 PascalCase 标识符；编译期只校验两种语言键集一致，
/// 编译器和现有自检都看不见「C# 里的键在资源里根本不存在」。
/// </para>
/// <para>
/// 这里钉住三件事：<see cref="LocalizationKey.Normalize"/> 的幂等换算、App 源码里所有字面键
/// 归一化后都必须命中资源、以及 <see cref="CrashFindingCode"/> 每个枚举值推导出的
/// title/detail/fix 键都齐全。
/// </para>
/// </summary>
public static class LocalizationKeyChecks
{
    /// <summary>源码里的取文案调用：<c>T("key")</c>，可选 <c>localizer.</c> / <c>Localizer.Instance.</c> 前缀。</summary>
    private static readonly Regex LiteralKeyCall = new(
        @"(?:localizer|Localizer\.Instance|T|L)\.?T\(\s*""([^""]+)""",
        RegexOptions.CultureInvariant);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        Console.WriteLine("[LocalizationKeyChecks] 文案键与资源名");

        CheckNormalize();

        var root = I18nResw.RepoRoot();
        var zh = I18nResw.Read(root, I18nResw.SimplifiedChinese);
        var en = I18nResw.Read(root, I18nResw.English);
        ChecksContext.Check("文案键: 能读到 zh-CN 资源表", zh.Count > 500, "读到 " + zh.Count + " 个键");
        ChecksContext.Check("文案键: 能读到 en-US 资源表", en.Count > 500, "读到 " + en.Count + " 个键");

        CheckSourceLiteralKeys(root, zh, en);
        CheckCrashFindingCodes(zh, en);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>归一化必须把点分键变成资源名，且对已经是标识符的输入幂等。</summary>
    private static void CheckNormalize()
    {
        ChecksContext.Equal(
            "文案键: 点分键转 PascalCase",
            "CrashSummaryGameDetail",
            LocalizationKey.Normalize("crash.summary.gameDetail"));
        ChecksContext.Equal(
            "文案键: 点分键第二段首字母也大写",
            "CrashFindingNoCrashBundleTitle",
            LocalizationKey.Normalize("crash.finding.noCrashBundle.title"));
        ChecksContext.Equal(
            "文案键: 异常码键含数字",
            "CrashExceptionC0000005",
            LocalizationKey.Normalize("crash.exception.c0000005"));
        ChecksContext.Equal(
            "文案键: 标识符原样返回（幂等）",
            "CrashSummaryGameDetail",
            LocalizationKey.Normalize(LocalizationKey.Normalize("crash.summary.gameDetail")));
    }

    /// <summary>
    /// 扫描 App 源码里的字面键：归一化后必须命中资源表。
    /// 只看字面量；运行期拼出来的键由 <see cref="CheckCrashFindingCodes"/> 覆盖。
    /// </summary>
    private static void CheckSourceLiteralKeys(
        string root,
        Dictionary<string, string> zh,
        Dictionary<string, string> en)
    {
        var appRoot = Path.Combine(root, "src", "PdxLauncher.App");
        var scanned = 0;
        var missing = new List<string>();

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            // bin/obj 里是构建产物与生成的 Localizer 属性，不参与扫描。
            var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(static segment => segment is "bin" or "obj"))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (Match match in LiteralKeyCall.Matches(text))
            {
                scanned++;
                var key = match.Groups[1].Value;
                var identifier = LocalizationKey.Normalize(key);
                if (!zh.ContainsKey(identifier) || !en.ContainsKey(identifier))
                {
                    var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                    missing.Add(Path.GetFileName(path) + ":" + line + " " + key);
                }
            }
        }

        // 下限断言：正则失效时不能空跑通过。
        // App 里绝大多数文案走生成属性（L.Xxx / localizer.Xxx），字面键调用只在少数
        // 需要拼参数或拼键名的地方出现，目前是 20 出头；这里取 15 留一点余量。
        ChecksContext.Check("文案键: 扫到足够多的字面键调用", scanned >= 15, "扫到 " + scanned + " 处");
        ChecksContext.Check(
            "文案键: App 源码里的字面键都在 zh-CN 与 en-US 中",
            missing.Count == 0,
            missing.Count == 0 ? null : string.Join("；", missing));
    }

    /// <summary>
    /// 崩溃分析按 <see cref="CrashFindingCode"/> 枚举推导资源名，编译器看不到组合，
    /// 这里把 31 个值 × title/detail/fix 全部展开断言一遍。
    /// </summary>
    private static void CheckCrashFindingCodes(Dictionary<string, string> zh, Dictionary<string, string> en)
    {
        var codes = Enum.GetValues<CrashFindingCode>();
        var missing = new List<string>();
        foreach (var code in codes)
        {
            var name = code.ToString();
            var prefix = "CrashFinding" + char.ToUpperInvariant(name[0]) + name[1..];
            foreach (var suffix in new[] { "Title", "Detail", "Fix" })
            {
                var key = prefix + suffix;
                if (!zh.ContainsKey(key) || !en.ContainsKey(key))
                {
                    missing.Add(key);
                }
            }
        }

        ChecksContext.Check(
            "文案键: 展开全部崩溃结论枚举",
            codes.Length >= 30,
            codes.Length + " 个结论码");
        ChecksContext.Check(
            "文案键: 每条崩溃结论的标题/说明/建议都有中英文案",
            missing.Count == 0,
            missing.Count == 0 ? null : string.Join("；", missing));

        var noGap = "CrashFindingModScriptErrorsNearCrashDetailNoGap";
        ChecksContext.Check(
            "文案键: 脚本错误无时间间隔说明存在",
            zh.ContainsKey(noGap) && en.ContainsKey(noGap));
    }
}
