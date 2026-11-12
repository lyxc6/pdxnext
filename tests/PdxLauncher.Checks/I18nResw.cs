namespace PdxLauncher.Checks;

using System.Text;
using System.Xml.Linq;

/// <summary>
/// 读取 WinUI 文案源 <c>src/PdxLauncher.App/Strings/&lt;lang&gt;/Resources.resw</c>。
/// <para>
/// 资源名是 PascalCase 标识符（.resw 的点是属性分隔符，资源名不能带点）。
/// 检查里仍可以写原来的点分键，用 <see cref="IdentifierFor"/> 转成资源名再查。
/// </para>
/// </summary>
internal static class I18nResw
{
    internal const string SimplifiedChinese = "zh-CN";
    internal const string English = "en-US";

    /// <summary>两种语言都必须有文案的固定语言列表。</summary>
    internal static readonly string[] Languages = [SimplifiedChinese, English];

    /// <summary>资源文件路径。</summary>
    internal static string ResourcesPath(string root, string language) =>
        Path.Combine(root, "src", "PdxLauncher.App", "Strings", language, "Resources.resw");

    /// <summary>点分键 → 资源名（每段首字母大写后拼接；已经是标识符的原样返回）。</summary>
    internal static string IdentifierFor(string key)
    {
        if (!key.Contains('.'))
        {
            return key;
        }

        var builder = new StringBuilder(key.Length);
        foreach (var segment in key.Split('.'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(segment[0]));
            builder.Append(segment, 1, segment.Length - 1);
        }

        return builder.ToString();
    }

    /// <summary>读入「资源名 → 文案」。文件缺失时返回空表（调用方自己断言存在性）。</summary>
    internal static Dictionary<string, string> Read(string root, string language)
    {
        var path = ResourcesPath(root, language);
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return table;
        }

        using var stream = File.OpenRead(path);
        var document = XDocument.Load(stream);
        foreach (var data in document.Root?.Elements("data") ?? Enumerable.Empty<XElement>())
        {
            var name = (string?)data.Attribute("name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            table[name] = data.Element("value")?.Value ?? string.Empty;
        }

        return table;
    }

    /// <summary>统一取文本，缺键返回 null。</summary>
    internal static string? Get(Dictionary<string, string> table, string key) =>
        table.TryGetValue(IdentifierFor(key), out var value) ? value : null;

    /// <summary>从检查程序输出目录往上找仓库根（以 PdxLauncher.sln 为哨兵，链接工作树里也成立）。</summary>
    internal static string RepoRoot()
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

        throw new DirectoryNotFoundException("从 " + AppContext.BaseDirectory + " 向上找不到 PdxLauncher.sln");
    }
}
