namespace PdxLauncher.Localization.Generator;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

/// <summary>
/// 从 <c>Strings/&lt;lang&gt;/Resources.resw</c> 生成界面文案的强类型入口。
/// 生成 <c>Localizer</c> 的 partial（每个资源一个只读属性）与 <c>Keys</c> 常量类。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class LocalizationGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        ReswReader.Register(context, Execute);
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<ResourceFile> files)
    {
        var tables = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.OrderBy(static item => item.Path, StringComparer.Ordinal))
        {
            var language = file.Language;
            if (language is null)
            {
                LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, file.Path, "无法从路径推断语言，应为 Strings/<lang>/Resources.resw");
                return;
            }

            var table = ReswReader.Read(context, file, language);
            if (table is null)
            {
                return;
            }

            if (tables.ContainsKey(language))
            {
                LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, language, "同一种语言出现多份 Resources.resw");
                return;
            }

            tables.Add(language, table);
        }

        if (tables.Count == 0)
        {
            LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, "Strings", "没有找到任何 Strings/<lang>/Resources.resw");
            return;
        }

        if (!Validate(context, tables))
        {
            return;
        }

        var names = tables.Values
            .SelectMany(static table => table.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal);
        LocalizerEmitter.AddTo(context, names);
    }

    private static bool Validate(
        SourceProductionContext context,
        Dictionary<string, SortedDictionary<string, string>> tables)
    {
        var reference = tables.OrderByDescending(static pair => pair.Value.Count).First();
        foreach (var pair in tables)
        {
            if (pair.Key.Equals(reference.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var missing = reference.Value.Keys.Except(pair.Value.Keys, StringComparer.Ordinal).ToList();
            var extra = pair.Value.Keys.Except(reference.Value.Keys, StringComparer.Ordinal).ToList();
            if (missing.Count > 0 || extra.Count > 0)
            {
                var detail = string.Format(
                    "{0} 与 {1} 键集不一致：缺少 {2} 个{3}；多出 {4} 个{5}",
                    pair.Key,
                    reference.Key,
                    missing.Count,
                    Describe(missing),
                    extra.Count,
                    Describe(extra));
                LocalizationDiagnostics.Report(context, LocalizationDiagnostics.KeySetMismatch, pair.Key, detail);
                return false;
            }

            foreach (var entry in pair.Value)
            {
                if (!LocalizerEmitter.Placeholders(entry.Value)
                        .SequenceEqual(LocalizerEmitter.Placeholders(reference.Value[entry.Key]), StringComparer.Ordinal))
                {
                    var detail = string.Format("键 {0} 的占位符在 {1} 与 {2} 不一致", entry.Key, pair.Key, reference.Key);
                    LocalizationDiagnostics.Report(context, LocalizationDiagnostics.InvalidResource, entry.Key, detail);
                    return false;
                }
            }
        }

        return true;
    }

    private static string Describe(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        return "（" + string.Join(", ", items.Take(8)) + "）";
    }
}
