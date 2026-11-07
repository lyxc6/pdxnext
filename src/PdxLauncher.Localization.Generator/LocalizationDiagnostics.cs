namespace PdxLauncher.Localization.Generator;

using Microsoft.CodeAnalysis;

/// <summary>生成器报告给编译器的诊断。</summary>
internal static class LocalizationDiagnostics
{
    internal static readonly DiagnosticDescriptor InvalidResource = new(
        id: "PDXI18N001",
        title: "文案资源不合法",
        messageFormat: "{0}",
        category: "PdxLauncher.Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor KeySetMismatch = new(
        id: "PDXI18N002",
        title: "两种语言的键集不一致",
        messageFormat: "{0}",
        category: "PdxLauncher.Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>把「作用域 + 细节」拼成一条诊断并上报。</summary>
    internal static void Report(
        SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        string scope,
        string detail)
    {
        var message = string.Format("[{0}] {1}", scope, detail);
        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, message));
    }
}
