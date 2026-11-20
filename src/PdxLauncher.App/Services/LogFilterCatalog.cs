namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;

/// <summary>日志页级别筛选下拉的一项；标题走本地化键，切语言时自动刷新。</summary>
public sealed class LogFilterOption : LocalizedObservableObject
{
    internal LogFilterOption(LogLevelFilter filter, string titleKey)
    {
        Filter = filter;
        _titleKey = titleKey;
    }

    private readonly string _titleKey;

    public LogLevelFilter Filter { get; }

    public string Title => T(_titleKey);

    public override string ToString() => Title;
}

/// <summary>级别筛选选项目录；Resolve 返回同一实例，保证 PdxDropDown 按引用相等能匹配。</summary>
public static class LogFilterCatalog
{
    public static IReadOnlyList<LogFilterOption> All { get; } = new[]
    {
        new LogFilterOption(LogLevelFilter.All, Keys.DiagnosticsLogFilterAll),
        new LogFilterOption(LogLevelFilter.Warning, Keys.DiagnosticsLogFilterWarn),
        new LogFilterOption(LogLevelFilter.Error, Keys.DiagnosticsLogFilterError),
    };

    public static LogFilterOption Resolve(LogLevelFilter filter) =>
        All.FirstOrDefault(option => option.Filter == filter) ?? All[0];
}
