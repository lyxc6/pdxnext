namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.CrashAnalysis;

/// <summary>
/// 「崩溃分析」页的视图模型：持有一次只读分析的原始结果，按当前语言重建摘要、结论、崩溃记录、
/// 日志聚类与启动链路。切语言只重建文案，不重新读文件；重新分析由页面显式触发。
/// </summary>
public sealed class CrashAnalysisViewModel : LocalizedObservableObject
{
    private CrashAnalysisReport? _report;
    private bool _hasGame;
    private bool _isBusy;
    private bool _onlyErrors;
    private string _selectedLogName = string.Empty;
    private DateTimeOffset? _analyzedAt;
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _statusMessage = string.Empty;
    private string _logTailText = string.Empty;
    private string _logSummaryText = string.Empty;
    private string _verdictTitle = string.Empty;
    private string _verdictMessage = string.Empty;
    private bool _hasCritical;
    private bool _hasWarning;
    private bool _hasClear;
    private string _modListHint = string.Empty;

    /// <summary>概览摘要卡。</summary>
    public ObservableCollection<CrashSummaryRowViewModel> SummaryRows { get; } = new();

    /// <summary>诊断结论卡。</summary>
    public ObservableCollection<CrashFindingRowViewModel> Findings { get; } = new();

    /// <summary>崩溃记录。</summary>
    public ObservableCollection<CrashBundleRowViewModel> Crashes { get; } = new();

    /// <summary>已启用 Mod 清单。</summary>
    public ObservableCollection<CrashModRowViewModel> Mods { get; } = new();

    /// <summary>日志文件下拉项。</summary>
    public ObservableCollection<CrashLogFileRowViewModel> LogFiles { get; } = new();

    /// <summary>错误聚类。</summary>
    public ObservableCollection<CrashLogGroupRowViewModel> Groups { get; } = new();

    /// <summary>启动链路。</summary>
    public ObservableCollection<CrashLaunchRowViewModel> LaunchRows { get; } = new();

    /// <summary>当前是否选中了游戏。</summary>
    public bool HasGame => _hasGame;

    /// <summary>没有选中游戏。</summary>
    public bool HasNoGame => !_hasGame;

    /// <summary>是否已有分析结果。</summary>
    public bool HasReport => _report is not null;

    /// <summary>选中了游戏但还没有结果（首次加载中或读取失败）。</summary>
    public bool HasNoReport => _hasGame && _report is null;

    /// <summary>分析进行中。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>是否有高风险结论。</summary>
    public bool HasCritical => _hasCritical;

    /// <summary>只有警告、没有高风险结论。</summary>
    public bool HasWarning => _hasWarning;

    /// <summary>没有高风险/警告结论。</summary>
    public bool HasClear => _hasClear;

    /// <summary>结论 InfoBar 的标题。</summary>
    public string VerdictTitle => _verdictTitle;

    /// <summary>结论 InfoBar 的正文。</summary>
    public string VerdictMessage => _verdictMessage;

    /// <summary>最近一次分析时间文案。</summary>
    public string LastAnalysisText => _analyzedAt is { } at
        ? T(Keys.CrashOverviewAnalyzed, CrashAnalysisText.FormatTime(at, L))
        : string.Empty;

    /// <summary>摘要卡是否为空。</summary>
    public bool HasFindings => Findings.Count > 0;

    /// <summary>是否有崩溃记录。</summary>
    public bool HasCrashes => Crashes.Count > 0;

    /// <summary>是否有 Mod 行。</summary>
    public bool HasMods => Mods.Count > 0;

    /// <summary>是否有日志文件。</summary>
    public bool HasLogFiles => LogFiles.Count > 0;

    /// <summary>是否有错误聚类。</summary>
    public bool HasGroups => Groups.Count > 0;

    /// <summary>是否有启动链路行。</summary>
    public bool HasLaunchRows => LaunchRows.Count > 0;

    /// <summary>日志尾部是否有内容。</summary>
    public bool HasLogTail => !string.IsNullOrEmpty(_logTailText);

    /// <summary>最近崩溃目录；没有时为空串。</summary>
    public string LatestCrashPath => _report?.LatestCrash?.Directory ?? string.Empty;

    /// <summary>游戏日志目录；没有结果时为空串。</summary>
    public string LogFolderPath => _report?.LaunchChain.LogDirectory ?? string.Empty;

    /// <summary>当前选中的日志文件名。</summary>
    public string SelectedLogName => _selectedLogName;

    /// <summary>
    /// 下拉框绑定的当前日志项。getter 按名称从 <see cref="LogFiles"/> 里取，
    /// 列表重建或切语言后会返回新的行对象，保证 PdxDropDown 显示当前语言的摘要。
    /// </summary>
    public CrashLogFileRowViewModel? SelectedLogFile
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_selectedLogName))
            {
                return null;
            }

            return LogFiles.FirstOrDefault(file =>
                string.Equals(file.Name, _selectedLogName, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (value is not null)
            {
                SelectLog(value.Name);
            }
        }
    }

    /// <summary>选中日志的摘要（文件名 / 大小 / 错误数）。</summary>
    public string LogSummaryText => _logSummaryText;

    /// <summary>只显示错误块。</summary>
    public bool OnlyErrors
    {
        get => _onlyErrors;
        set
        {
            if (SetProperty(ref _onlyErrors, value))
            {
                UpdateLogTail();
            }
        }
    }

    /// <summary>日志尾部文本（最新在最前）。</summary>
    public string LogTailText => _logTailText;

    /// <summary>Mod 列表的边界说明。</summary>
    public string ModListHint => _modListHint;

    /// <summary>复制到剪贴板的报告文本。</summary>
    public string CopyReportText => _report is null
        ? string.Empty
        : CrashAnalysisText.BuildReportText(_report, L);

    /// <summary>页面底部状态行。</summary>
    public string StatusMessage => _statusMessage;

    /// <summary>是否有状态行。</summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    /// <summary>重新读取并分析当前游戏。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var game = AppServices.CurrentGame;
            _hasGame = game is not null;
            if (game is null)
            {
                _report = null;
                _analyzedAt = null;
                RebuildAll();
                return;
            }

            var report = await Task.Run(
                () => AppServices.CrashAnalysis.AnalyzeAsync(game, AppServices.LogPath, cancellationToken),
                cancellationToken).ConfigureAwait(true);

            _report = report;
            _analyzedAt = report.AnalyzedAt;
            if (string.IsNullOrWhiteSpace(_selectedLogName)
                || report.LogFiles.All(file => !string.Equals(file.Name, _selectedLogName, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedLogName = PickDefaultLog(report);
            }

            RebuildAll();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppServices.Logger.Error("崩溃分析失败", exception);
            _report = null;
            _analyzedAt = null;
            SetStatus(Keys.CrashStatusLoadFailed, exception.Message);
            RebuildAll();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>切换日志文件。</summary>
    public void SelectLog(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (string.Equals(_selectedLogName, name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedLogName = name;
        UpdateLogTail();
        OnPropertyChanged(nameof(SelectedLogName));
        OnPropertyChanged(nameof(SelectedLogFile));
    }

    /// <summary>设置底部状态行。</summary>
    public void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        _statusMessage = T(key, args);
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    /// <summary>清空底部状态行。</summary>
    public void ClearStatus()
    {
        _statusKey = null;
        _statusArgs = Array.Empty<object?>();
        _statusMessage = string.Empty;
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    /// <inheritdoc />
    protected override void Relocalize()
    {
        if (_report is not null)
        {
            RebuildAll();
        }

        if (_statusKey is not null)
        {
            _statusMessage = T(_statusKey, _statusArgs);
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    private void RebuildAll()
    {
        SummaryRows.Clear();
        Findings.Clear();
        Crashes.Clear();
        Mods.Clear();
        LogFiles.Clear();
        Groups.Clear();
        LaunchRows.Clear();
        _logTailText = string.Empty;
        _logSummaryText = string.Empty;
        _verdictTitle = string.Empty;
        _verdictMessage = string.Empty;
        _hasCritical = false;
        _hasWarning = false;
        _hasClear = false;
        _modListHint = string.Empty;

        var report = _report;
        if (report is null)
        {
            NotifyDerivedState();
            return;
        }

        foreach (var row in CrashAnalysisText.CreateSummaryRows(report, L))
        {
            SummaryRows.Add(row);
        }

        foreach (var row in CrashAnalysisText.CreateFindingRows(report, L))
        {
            Findings.Add(row);
        }

        foreach (var row in CrashAnalysisText.CreateBundleRows(report, L))
        {
            Crashes.Add(row);
        }

        foreach (var row in CrashAnalysisText.CreateModRows(report, L))
        {
            Mods.Add(row);
        }

        foreach (var row in CrashAnalysisText.CreateLogFileRows(report, L))
        {
            LogFiles.Add(row);
        }

        foreach (var row in CrashAnalysisText.CreateGroupRows(report, L))
        {
            Groups.Add(row);
        }

        foreach (var row in CrashAnalysisText.CreateLaunchRows(report, L))
        {
            LaunchRows.Add(row);
        }

        var topFinding = Findings.FirstOrDefault();
        _hasCritical = Findings.Any(static row => row.IsCritical);
        _hasWarning = !_hasCritical && Findings.Any(static row => row.IsWarning);
        _hasClear = !_hasCritical && !_hasWarning;

        if (topFinding is not null)
        {
            _verdictTitle = topFinding.Title;
            _verdictMessage = topFinding.Detail;
        }
        else if (report.HasCrash)
        {
            _verdictTitle = T(Keys.CrashVerdictUnknownTitle);
            _verdictMessage = T(
                Keys.CrashVerdictUnknownDetail,
                report.LatestCrash?.ExceptionDisplay ?? T(Keys.CrashSummaryExceptionUnknown));
        }
        else
        {
            _verdictTitle = T(Keys.CrashOverviewVerdictClearTitle);
            _verdictMessage = T(Keys.CrashOverviewVerdictClearDetail);
        }

        _modListHint = report.ModListAvailable ? string.Empty : T(Keys.CrashOverviewModsUnknown);

        if (LogFiles.Count > 0 && string.IsNullOrWhiteSpace(_selectedLogName))
        {
            _selectedLogName = PickDefaultLog(report);
        }

        UpdateLogTail(notify: false);
        NotifyDerivedState();
    }

    private void UpdateLogTail(bool notify = true)
    {
        var selected = LogFiles.FirstOrDefault(file =>
            string.Equals(file.Name, _selectedLogName, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            _logTailText = string.Empty;
            _logSummaryText = string.Empty;
            if (notify)
            {
                OnPropertyChanged(nameof(LogTailText));
                OnPropertyChanged(nameof(LogSummaryText));
                OnPropertyChanged(nameof(HasLogTail));
                OnPropertyChanged(nameof(SelectedLogName));
                OnPropertyChanged(nameof(SelectedLogFile));
            }

            return;
        }

        string raw = LogTailReader.ReadTail(selected.Path, maxLines: 400, maxBytes: 512 * 1024);
        _logTailText = CrashAnalysisText.BuildLogTailText(raw, OnlyErrors);
        _logSummaryText = selected.DisplayText;
        if (notify)
        {
            OnPropertyChanged(nameof(LogTailText));
            OnPropertyChanged(nameof(LogSummaryText));
            OnPropertyChanged(nameof(HasLogTail));
            OnPropertyChanged(nameof(SelectedLogName));
            OnPropertyChanged(nameof(SelectedLogFile));
        }
    }

    private static string PickDefaultLog(CrashAnalysisReport report)
    {
        var error = report.LogFiles.FirstOrDefault(static file =>
            string.Equals(file.Name, "error.log", StringComparison.OrdinalIgnoreCase));
        return error?.Name ?? report.LogFiles.FirstOrDefault()?.Name ?? string.Empty;
    }

    private void NotifyDerivedState()
    {
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(HasNoGame));
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(HasNoReport));
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(HasCrashes));
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(HasLogFiles));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasLaunchRows));
        OnPropertyChanged(nameof(HasLogTail));
        OnPropertyChanged(nameof(HasCritical));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(HasClear));
        OnPropertyChanged(nameof(VerdictTitle));
        OnPropertyChanged(nameof(VerdictMessage));
        OnPropertyChanged(nameof(LastAnalysisText));
        OnPropertyChanged(nameof(LatestCrashPath));
        OnPropertyChanged(nameof(LogFolderPath));
        OnPropertyChanged(nameof(CopyReportText));
        OnPropertyChanged(nameof(ModListHint));
        OnPropertyChanged(nameof(SelectedLogName));
        OnPropertyChanged(nameof(SelectedLogFile));
        OnPropertyChanged(nameof(LogSummaryText));
        OnPropertyChanged(nameof(LogTailText));
    }
}
