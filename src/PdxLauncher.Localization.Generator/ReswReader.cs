namespace PdxLauncher.Localization.Generator;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>一份 Resources.resw 及其语言。</summary>
internal sealed class ResourceFile
{
    internal ResourceFile(string path, string content)
    {
        Path = path;
        Content = content;
    }

    internal string Path { get; }

    internal string Content { get; }

    /// <summary>语言取 <c>Strings/&lt;lang&gt;/Resources.resw</c> 的目录名。</summary>
    internal string? Language
    {
        get
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            var language = directory is null ? null : System.IO.Path.GetFileName(directory);
            return string.IsNullOrWhiteSpace(language) ? null : language;
        }
    }
}

/// <summary>.resw 解析与校验。</summary>
internal static class ReswReader
{
    private static readonly Regex IdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    internal static bool IsResourceFile(string path) =>
        path.Replace('\\', '/').EndsWith("/Resources.resw", StringComparison.OrdinalIgnoreCase);

    internal static ResourceFile Load(AdditionalText file, CancellationToken cancellationToken) =>
        new ResourceFile(file.Path, file.GetText(cancellationToken)?.ToString() ?? string.Empty);

    /// <summary>收集所有 AdditionalFiles 里的 Resources.resw。</summary>
    internal static IncrementalValueProvider<ImmutableArray<ResourceFile>> LoadAll(
        IncrementalGeneratorInitializationContext context)
    {
        return context.AdditionalTextsProvider
            .Where(static file => IsResourceFile(file.Path))
            .Select(static (file, cancellationToken) => Load(file, cancellationToken))
            .Collect();
    }

    /// <summary>把收集结果接到生成回调上。</summary>
    internal static void Register(
        IncrementalGeneratorInitializationContext context,
        Action<SourceProductionContext, ImmutableArray<ResourceFile>> action)
    {
        context.RegisterSourceOutput(LoadAll(context), (production, items) => action(production, items));
    }

    internal static SortedDictionary<string, string>? Read(
        SourceProductionContext context,
        ResourceFile file,
        string language)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(file.Content);
        }
        catch (XmlException error)
        {
            LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, file.Path, "不是合法 XML：" + error.Message);
            return null;
        }

        var table = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in document.Root?.Elements("data") ?? Enumerable.Empty<XElement>())
        {
            var name = (string?)data.Attribute("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!IdentifierPattern.IsMatch(name))
            {
                LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, language, "资源名必须是不含点的标识符：" + name);
                return null;
            }

            var value = data.Element("value")?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, language, "资源值为空白：" + name);
                return null;
            }

            if (table.ContainsKey(name!))
            {
                LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, language, "资源重复：" + name);
                return null;
            }

            table.Add(name!, value);
        }

        if (table.Count == 0)
        {
            LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, file.Path, "文件里没有任何 data 项");
            return null;
        }

        return table;
    }
}
