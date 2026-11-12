namespace PdxLauncher.Checks;

using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 文案绑定自检：带占位符的文案键不能被裸绑到 <c>{x:Bind L.Xxx}</c>。
/// <para>
/// 真机踩过一次：列表徽标写 <c>{x:Bind L.ModstudioRowPublished}</c>，而
/// <c>modstudio.rowPublished</c> 的值是「已发布 · {0}」。生成的属性是
/// <c>Format("modstudio.rowPublished")</c>，无参时 <c>Localizer.Format</c> 直接返回模板原文
/// （它只在 <c>args</c> 非空时才 <c>string.Format</c>），于是界面上原样出现 <c>{0}</c>。
/// </para>
/// <para>
/// 这类错编译能过、自检也过，只能靠读源码钉住：凡是 XAML 里裸绑 <c>L.*</c> 的键，
/// 模板里都不允许出现 <c>{n}</c>；要用占位符就必须在视图模型里用 <c>T(Keys.X, args)</c> 拼好再绑。
/// </para>
/// </summary>
public static class LocalizationBindingChecks
{
    /// <summary>XAML 里裸绑文案属性的写法：<c>{x:Bind L.SomeKey ...}</c>。</summary>
    private static readonly Regex BareBinding = new(
        @"\{x:Bind\s+L\.([A-Za-z0-9_]+)",
        RegexOptions.CultureInvariant);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var templates = LoadTemplates(root);

        ChecksContext.Check(
            "文案绑定: 能从 zh-CN 文案源读到键表",
            templates.Count > 500,
            "读到 " + templates.Count + " 个键");

        var checkedBindings = 0;
        foreach (var xamlPath in EnumerateXaml(root))
        {
            var text = File.ReadAllText(xamlPath);
            foreach (Match match in BareBinding.Matches(text))
            {
                var identifier = match.Groups[1].Value;
                if (!templates.TryGetValue(identifier, out var template))
                {
                    // 不是文案属性（例如某个视图模型自己的 L 之外的成员）就跳过，不做误报。
                    continue;
                }

                checkedBindings++;
                var placeholders = Regex.Matches(template, @"\{\d+\}");
                ChecksContext.Check(
                    "文案绑定: " + Path.GetFileName(xamlPath) + " 里的 L." + identifier + " 没有占位符",
                    placeholders.Count == 0,
                    placeholders.Count == 0 ? null : "模板是「" + template + "」");
            }
        }

        ChecksContext.Check(
            "文案绑定: 至少扫到一批裸绑定",
            checkedBindings > 20,
            "扫到 " + checkedBindings + " 处");

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>标识符 → 模板原文（资源名本身就是标识符）。</summary>
    private static Dictionary<string, string> LoadTemplates(string root) => I18nResw.Read(root, "zh-CN");

    private static IEnumerable<string> EnumerateXaml(string root)
    {
        var appRoot = Path.Combine(root, "src", "PdxLauncher.App");
        foreach (var name in new[] { "Views", "Controls" })
        {
            var directory = Path.Combine(appRoot, name);
            if (Directory.Exists(directory))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.xaml", SearchOption.AllDirectories))
                {
                    yield return path;
                }
            }
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
