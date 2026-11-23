namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.CrashAnalysis;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Online;

/// <summary>
/// 「性能与日志」页的视图模型：五类只读诊断（概览指标、缓存占用、日志尾部、限流状态、启动耗时）。
/// 所有采样都只读本地状态与文件，不联网、不写游戏目录；单类采样失败只记日志并留空，不抛到页面。
/// 数字与拼接句子在下次采样或切语言时重建，界面只做绑定。
/// </summary>
public sealed class DiagnosticsViewModel : LocalizedObservableObject
{
    /// <summary>日志尾部最多显示的行数（与 <see cref="LogTailReader.DefaultMaxLines"/> 一致）。</summary>
    public const int LogTailLines = LogTailReader.DefaultMaxLines;

    private MemoryDiagnostics.MemorySnapshot? _snapshot;
    private IReadOnlyDictionary<string, HostStats> _hosts = new Dictionary<string, HostStats>(StringComparer.OrdinalIgnoreCase);
    private ProxyResolution? _proxy;
    private LogMetrics _logMetrics = LogMetrics.Empty;
    private string _rawTail = string.Empty;
    private IReadOnlyList<LogDisplayLine> _logDisplayLines = Array.Empty<LogDisplayLine>();
    private string _logTailEmptyText = string.Empty;
    private string _logSummaryText = string.Empty;
    private string _gameLogStatusText = string.Empty;
    private string _logRotationText = string.Empty;
    private string _logLastWriteText = string.Empty;
    private string _proxyText = string.Empty;
    private string _proxyWarningText = string.Empty;
    private string _startupTotalText = string.Empty;
    private string _lastSampleText = string.Empty;
    private long _cacheBytes;
    private int _workshopEntries;
    private IReadOnlyList<StartupMark> _marks = Array.Empty<StartupMark>();
    private LogLevelFilter _logFilter = LogLevelFilter.All;
    private LogFilterOption _selectedLogFilterOption = LogFilterCatalog.All[0];
    private bool _logWrap;
    private bool _autoRefresh;
    private DateTimeOffset? _sampledAt;
    private bool _isBusy;
    private bool _isCacheBusy;
    private bool _isLogBusy;
    private bool _live = true;
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _statusMessage = string.Empty;

    public DiagnosticsViewModel()
    {
        _logWrap = AppServices.IsInitialized && AppServices.Settings.DiagnosticsLogWrap;
        _autoRefresh = AppServices.IsInitialized && AppServices.Settings.DiagnosticsLogAutoRefresh;
        CacheRows = new ObservableCollection<CacheRowViewModel>(CacheRowCatalog.Create());
        RebuildMetrics();
        RebuildGauges();
        RebuildStartup();
        UpdateLogTexts();
        RebuildNetwork();
    }

    /// <summary>七类联网缓存，与设置页共用同一份行定义。</summary>
    public ObservableCollection<CacheRowViewModel> CacheRows { get; }

    /// <summary>概览指标卡（工作集、托管堆、GC、句柄…）。</summary>
    public ObservableCollection<DiagnosticMetricViewModel> Metrics { get; } = new();

    /// <summary>概览的四个核心仪表环（内存 / 缓存 / 日志 / 启动耗时）。</summary>
    public ObservableCollection<GaugeViewModel> Gauges { get; } = new();

    /// <summary>按严重度排好的优化建议。</summary>
    public ObservableCollection<DiagnosticHintViewModel> Hints { get; } = new();

    /// <summary>每个发过请求的 host 一行。</summary>
    public ObservableCollection<NetworkHostRowViewModel> NetworkRows { get; } = new();

    /// <summary>启动里程碑（进程启动 → 首页加载）。</summary>
    public ObservableCollection<StartupMarkRowViewModel> StartupRows { get; } = new();

    /// <summary>概览采样中（重采样按钮旁的小圈用它）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsCacheBusy
    {
        get => _isCacheBusy;
        private set => SetProperty(ref _isCacheBusy, value);
    }

    public bool IsLogBusy
    {
        get => _isLogBusy;
        private set => SetProperty(ref _isLogBusy, value);
    }

    public bool HasMetrics => Metrics.Count > 0;

    public bool HasHints => Hints.Count > 0;

    public bool HasNetworkRows => NetworkRows.Count > 0;

    public bool HasStartupMarks => StartupRows.Count > 0;

    public bool HasLogTail => _logDisplayLines.Count > 0;

    public bool HasProxyWarning => !string.IsNullOrWhiteSpace(ProxyWarningText);

    /// <summary>合计占用文案（只统计字节，文件数在每行里）。</summary>
    public string CacheTotalText => T(Keys.SettingsCacheSummary, SizeFormatter.Format(_cacheBytes));

    /// <summary>最近一次采样的本地时间。</summary>
    public string LastSampleText => _sampledAt is { } at
        ? T(Keys.DiagnosticsOverviewUpdated, at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : string.Empty;

    /// <summary>实时刷新开关（由页面读写，默认开；关掉后概览不再定时采样）。</summary>
    public bool LiveEnabled
    {
        get => _live;
        set
        {
            if (SetProperty(ref _live, value))
            {
                OnPropertyChanged(nameof(LiveStatusText));
            }
        }
    }

    /// <summary>实时状态文案（实时中 / 已暂停），供概览标题行显示。</summary>
    public string LiveStatusText => T(LiveEnabled ? Keys.DiagnosticsGaugeLiveOn : Keys.DiagnosticsGaugeLiveOff);

    /// <summary>日志占用摘要：当前大小 / 归档个数 / 合计。</summary>
    public string LogSummaryText => _logSummaryText;

    /// <summary>当前游戏 error.log 的只读状态（正常 / 被占用 / 无权限 / 只读 / 不存在）。</summary>
    public string GameLogStatusText => _gameLogStatusText;

    /// <summary>轮转规则说明（阈值取自 <see cref="FileLogger"/> 的公开常量）。</summary>
    public string LogRotationText => _logRotationText;

    /// <summary>主文件最后写入时间。</summary>
    public string LogLastWriteText => _logLastWriteText;

    /// <summary>日志显示行（最新一条在最前；异常堆栈续行跟随所属块）。</summary>
    public IReadOnlyList<LogDisplayLine> LogDisplayLines => _logDisplayLines;

    /// <summary>无日志时的占位文案；有日志时为空串。</summary>
    public string LogTailEmptyText => _logTailEmptyText;

    /// <summary>日志尾部小标题（「最近 N 行（最新在上）」）。</summary>
    public string LogTailTitleText => T(Keys.DiagnosticsLogTailTitle, LogTailLines);

    /// <summary>级别筛选下拉的选项（全部 / 警告及以上 / 仅错误）。</summary>
    public IReadOnlyList<LogFilterOption> LogFilterOptions => LogFilterCatalog.All;

    /// <summary>下拉当前选中项；setter 同步过滤档位并重算日志行。</summary>
    public LogFilterOption SelectedLogFilterOption
    {
        get => _selectedLogFilterOption;
        set
        {
            if (value is null || ReferenceEquals(_selectedLogFilterOption, value))
            {
                return;
            }

            _selectedLogFilterOption = value;
            OnPropertyChanged(nameof(SelectedLogFilterOption));
            LogFilter = value.Filter;
        }
    }

    /// <summary>档位过滤：全部 / 警告及以上 / 仅错误。</summary>
    public LogLevelFilter LogFilter
    {
        get => _logFilter;
        set
        {
            if (!SetProperty(ref _logFilter, value))
            {
                return;
            }

            var option = LogFilterCatalog.Resolve(value);
            if (!ReferenceEquals(_selectedLogFilterOption, option))
            {
                _selectedLogFilterOption = option;
                OnPropertyChanged(nameof(SelectedLogFilterOption));
            }

            RebuildLogDisplayLines();
        }
    }

    /// <summary>
    /// 日志尾部是否自动换行。默认关（保持长行横向滚动的观感），打开后长行折行；
    /// 只影响显示方式，不改日志内容，状态写回设置后重启保留。
    /// </summary>
    public bool LogWrap
    {
        get => _logWrap;
        set
        {
            if (_logWrap == value)
            {
                return;
            }

            _logWrap = value;
            OnPropertyChanged(nameof(LogWrap));
            PersistLogWrap(value);
        }
    }

    /// <summary>
    /// 日志尾部是否每 5 秒自动刷新。与 <see cref="LogWrap"/> 对齐：TwoWay 绑定 + 设置持久化，
    /// 页面进入日志分区时按此状态启停定时器（定时器在页面代码里，避免 VM 引用 DispatcherTimer）。
    /// </summary>
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (_autoRefresh == value)
            {
                return;
            }

            _autoRefresh = value;
            OnPropertyChanged(nameof(AutoRefresh));
            PersistAutoRefresh(value);
        }
    }

    /// <summary>把自动刷新开关写回设置；失败只记日志，开关仍然即时生效。</summary>
    private static void PersistAutoRefresh(bool value)
    {
        try
        {
            if (!AppServices.IsInitialized)
            {
                return;
            }

            AppServices.Settings.DiagnosticsLogAutoRefresh = value;
            var saved = AppServices.SaveSettings();
            if (!saved.IsSuccess)
            {
                AppServices.Logger.Warn("保存日志自动刷新设置失败: " + saved.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("保存日志自动刷新设置失败: " + ex.Message);
        }
    }

    /// <summary>把换行开关写回设置；失败只记日志，开关仍然即时生效。</summary>
    private static void PersistLogWrap(bool value)
    {
        try
        {
            if (!AppServices.IsInitialized)
            {
                return;
            }

            AppServices.Settings.DiagnosticsLogWrap = value;
            var saved = AppServices.SaveSettings();
            if (!saved.IsSuccess)
            {
                AppServices.Logger.Warn("保存日志自动换行设置失败: " + saved.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("保存日志自动换行设置失败: " + ex.Message);
        }
    }

    /// <summary>代理回显（直连或「地址（来自来源）」）。</summary>
    public string ProxyText => _proxyText;

    /// <summary>代理配置有问题时的原因；正常为空，卡片随之隐藏。</summary>
    public string ProxyWarningText => _proxyWarningText;

    /// <summary>冷启动到可交互的总耗时。</summary>
    public string StartupTotalText => _startupTotalText;

    /// <summary>最近一次操作的结果（清除缓存等）。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    /// <summary>首次进入页面时把五个分区都采样一遍（各自失败互不影响）。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            await RefreshCacheStatsAsync(cancellationToken).ConfigureAwait(true);
            await RefreshLogAsync(cancellationToken).ConfigureAwait(true);
            await RefreshNetworkAsync(cancellationToken).ConfigureAwait(true);
            RefreshStartup();
            await SampleAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>重新采一次内存 / 进程快照，并重算优化建议。</summary>
    public async Task SampleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await Task.Run(
                () => MemoryDiagnostics.Capture(() => (
                    AppServices.WorkshopInfo.Count,
                    AppServices.WorkshopItemPages.Count,
                    AppServices.ModThumbnails.Cache.Count)),
                cancellationToken).ConfigureAwait(true);

            _snapshot = snapshot;
            _workshopEntries = snapshot.WorkshopItems + snapshot.WorkshopPages;
            _sampledAt = DateTimeOffset.Now;
            RebuildMetrics();
            OnPropertyChanged(nameof(LastSampleText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("性能页采集进程快照失败", ex);
        }

        RefreshHints();
        RebuildGauges();
    }

    /// <summary>重新统计七类缓存占用；只读文件属性，不联网。</summary>
    public async Task RefreshCacheStatsAsync(CancellationToken cancellationToken = default)
    {
        IsCacheBusy = true;
        try
        {
            var rows = CacheRows.ToArray();
            var usages = await CacheRowCatalog.MeasureAsync(rows, cancellationToken).ConfigureAwait(true);

            long totalBytes = 0;
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index].UpdateUsage(usages[index]);
                totalBytes += usages[index].Bytes;
            }

            _cacheBytes = totalBytes;
            _workshopEntries = AppServices.WorkshopInfo.Count + AppServices.WorkshopItemPages.Count;
            OnPropertyChanged(nameof(CacheTotalText));
            RefreshHints();
            RebuildGauges();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("性能页统计缓存占用失败", ex);
        }
        finally
        {
            IsCacheBusy = false;
        }
    }

    /// <summary>清除一类缓存，然后重算占用并回写状态栏。</summary>
    public async Task ClearCacheAsync(CacheRowViewModel row, CancellationToken cancellationToken)
    {
        if (row.IsBusy)
        {
            return;
        }

        var before = row.Usage;
        row.SetBusy(true);
        try
        {
            await Task.Run(row.Clear, cancellationToken).ConfigureAwait(true);
            await RefreshCacheStatsAsync(cancellationToken).ConfigureAwait(true);
            SetStatus(Keys.SettingsCacheStatusCleared, row.Title, SizeFormatter.Format(before.Bytes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("性能页清除缓存失败（" + row.Key + "）", ex);
            SetStatus(Keys.SettingsCacheStatusFailed, ex.Message);
        }
        finally
        {
            row.SetBusy(false);
        }
    }

    /// <summary>清除全部七类缓存（确认框由页面负责）。</summary>
    public async Task ClearAllCachesAsync(CancellationToken cancellationToken)
    {
        var before = _cacheBytes;
        try
        {
            await CacheRowCatalog.ClearAllAsync(CacheRows, cancellationToken).ConfigureAwait(true);
            await RefreshCacheStatsAsync(cancellationToken).ConfigureAwait(true);
            SetStatus(Keys.SettingsCacheStatusClearedAll, SizeFormatter.Format(before));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("性能页清除全部缓存失败", ex);
            SetStatus(Keys.SettingsCacheStatusFailed, ex.Message);
        }
    }

    /// <summary>重读日志大小与尾部（自动刷新定时器也走这里）。</summary>
    public async Task RefreshLogAsync(CancellationToken cancellationToken = default)
    {
        IsLogBusy = true;
        try
        {
            string? path = AppServices.LogPath;
            var metrics = await Task.Run(() => LogTailReader.Measure(path), cancellationToken).ConfigureAwait(true);
            var tail = await Task.Run(() => LogTailReader.ReadTail(path), cancellationToken).ConfigureAwait(true);

            _logMetrics = metrics;
            _rawTail = tail;
            UpdateLogTexts();
            RefreshHints();
            RebuildGauges();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("性能页读取日志失败", ex);
        }
        finally
        {
            IsLogBusy = false;
        }
    }

    /// <summary>重新取限流快照与代理解析结果。</summary>
    public async Task RefreshNetworkAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _hosts = AppServices.HttpThrottle.Snapshot();
            _proxy = await Task.Run(() => ProxyResolver.Describe(AppServices.Settings), cancellationToken).ConfigureAwait(true);
            RebuildNetwork();
            RefreshHints();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("性能页读取网络状态失败", ex);
        }
    }

    /// <summary>读取启动里程碑（启动时由 App / MainWindow 埋点，进程内只读）。</summary>
    public void RefreshStartup()
    {
        try
        {
            _marks = StartupTimeline.Snapshot();
        }
        catch (Exception ex)
        {
            _marks = Array.Empty<StartupMark>();
            AppServices.Logger.Warn("性能页读取启动耗时失败: " + ex.Message);
        }

        RebuildStartup();
        RefreshHints();
        RebuildGauges();
    }

    /// <summary>回写状态栏文案（记住键与参数，切语言后按新语言重建）。</summary>
    public void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = T(key, args);
    }

    protected override void Relocalize()
    {
        RebuildMetrics();
        RebuildGauges();
        RebuildNetwork();
        RebuildStartup();
        UpdateLogTexts();
        RefreshHints();
        OnPropertyChanged(nameof(CacheTotalText));
        OnPropertyChanged(nameof(LastSampleText));
        OnPropertyChanged(nameof(LiveStatusText));
        if (_statusKey is not null)
        {
            StatusMessage = T(_statusKey, _statusArgs);
        }
    }

    /// <summary>
    /// 就地更新概览的四个核心仪表环（内存 / 缓存 / 日志 / 启动耗时）。
    /// 不 Clear+重建：DataTemplate 里的 PdxGaugeRing 必须一直挂在视觉树上，补间动画才生效。
    /// </summary>
    private void RebuildGauges()
    {
        var workingSet = _snapshot?.WorkingSetBytes ?? 0;
        ApplyGauge(
            "memory",
            Keys.DiagnosticsGaugeMemory,
            SizeFormatter.Format(workingSet),
            PerformanceGaugeMath.ComputeMemory(workingSet));

        ApplyGauge(
            "cache",
            Keys.DiagnosticsGaugeCache,
            SizeFormatter.Format(_cacheBytes),
            PerformanceGaugeMath.ComputeCache(_cacheBytes));

        ApplyGauge(
            "log",
            Keys.DiagnosticsGaugeLog,
            SizeFormatter.Format(_logMetrics.CurrentBytes),
            PerformanceGaugeMath.ComputeLog(_logMetrics.CurrentBytes));

        double startupMs = _marks.Count == 0 ? 0 : _marks[^1].ElapsedMilliseconds;
        ApplyGauge(
            "startup",
            Keys.DiagnosticsGaugeStartup,
            startupMs <= 0 ? T(Keys.DiagnosticsStartupEmpty) : FormatDuration(TimeSpan.FromMilliseconds(startupMs)),
            PerformanceGaugeMath.ComputeStartup(startupMs));
    }

    private void ApplyGauge(string key, string titleKey, string valueText, GaugeReading reading)
    {
        var title = T(titleKey);
        var detail = T(Keys.DiagnosticsGaugePercent, Math.Round(reading.Percent).ToString(CultureInfo.InvariantCulture));
        var automationId = "diagGauge" + Pascalize(key);

        var existing = Gauges.FirstOrDefault(gauge => gauge.Key == key);
        if (existing is { } gauge)
        {
            gauge.Apply(reading.Percent, valueText, detail, reading.Status);
        }
        else
        {
            Gauges.Add(new GaugeViewModel(
                key,
                title,
                valueText,
                detail,
                reading.Percent,
                reading.Status,
                automationId));
        }
    }

    private void RebuildMetrics()
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            Metrics.Clear();
            OnPropertyChanged(nameof(HasMetrics));
            return;
        }

        ApplyMetric("workingSet", Keys.DiagnosticsMetricWorkingSet, Keys.DiagnosticsMetricWorkingSetDetail, SizeFormatter.Format(snapshot.WorkingSetBytes));
        ApplyMetric("private", Keys.DiagnosticsMetricPrivate, Keys.DiagnosticsMetricPrivateDetail, SizeFormatter.Format(snapshot.PrivateBytes));
        ApplyMetric("managed", Keys.DiagnosticsMetricManaged, Keys.DiagnosticsMetricManagedDetail, SizeFormatter.Format(snapshot.ManagedBytes));
        ApplyMetric("peak", Keys.DiagnosticsMetricPeak, Keys.DiagnosticsMetricPeakDetail, SizeFormatter.Format(snapshot.PeakWorkingSetBytes));
        ApplyMetric(
            "gc",
            Keys.DiagnosticsMetricGc,
            Keys.DiagnosticsMetricGcDetail,
            snapshot.Gen0.ToString(CultureInfo.InvariantCulture) + " / "
                + snapshot.Gen1.ToString(CultureInfo.InvariantCulture) + " / "
                + snapshot.Gen2.ToString(CultureInfo.InvariantCulture));
        ApplyMetric(
            "threads",
            Keys.DiagnosticsMetricThreads,
            Keys.DiagnosticsMetricThreadsDetail,
            snapshot.ThreadCount.ToString(CultureInfo.InvariantCulture) + " / "
                + snapshot.HandleCount.ToString(CultureInfo.InvariantCulture));
        ApplyMetric("uptime", Keys.DiagnosticsMetricUptime, Keys.DiagnosticsMetricUptimeDetail, FormatDuration(snapshot.Uptime));
        ApplyMetric(
            "workshop",
            Keys.DiagnosticsMetricWorkshop,
            Keys.DiagnosticsMetricWorkshopDetail,
            snapshot.WorkshopItems.ToString(CultureInfo.InvariantCulture) + " / "
                + snapshot.WorkshopPages.ToString(CultureInfo.InvariantCulture));
        ApplyMetric(
            "thumbnails",
            Keys.DiagnosticsMetricThumbnails,
            Keys.DiagnosticsMetricThumbnailsDetail,
            snapshot.ModThumbnails.ToString(CultureInfo.InvariantCulture));
    }

    private void ApplyMetric(string key, string titleKey, string detailKey, string value)
    {
        var title = T(titleKey);
        var detail = T(detailKey);
        var automationId = "diagMetric" + Pascalize(key);
        var existing = Metrics.FirstOrDefault(metric => metric.Key == key);
        if (existing is { } metric)
        {
            metric.Apply(title, value, detail);
        }
        else
        {
            Metrics.Add(new DiagnosticMetricViewModel(key, title, value, detail, automationId));
        }

        OnPropertyChanged(nameof(HasMetrics));
    }

    private void RebuildNetwork()
    {
        NetworkRows.Clear();
        foreach (var pair in _hosts
                     .OrderByDescending(item => item.Value.Rejected)
                     .ThenByDescending(item => item.Value.Throttled)
                     .ThenByDescending(item => item.Value.Total)
                     .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var stats = pair.Value;
            NetworkRows.Add(new NetworkHostRowViewModel(
                pair.Key,
                T(Keys.DiagnosticsNetworkStats, stats.Total, stats.Throttled, stats.Rejected),
                stats.LastRequestUtc == default
                    ? T(Keys.DiagnosticsNetworkLastRequestEmpty)
                    : T(Keys.DiagnosticsNetworkLastRequest, stats.LastRequestUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
                stats.InCooldown
                    ? T(Keys.DiagnosticsNetworkCooldown, FormatDuration(stats.CooldownRemaining))
                    : T(Keys.DiagnosticsNetworkCooldownNone),
                "diagHost" + NetworkRows.Count.ToString(CultureInfo.InvariantCulture)));
        }

        var proxy = _proxy;
        if (proxy is null)
        {
            _proxyText = T(Keys.CommonUnknown);
            _proxyWarningText = string.Empty;
        }
        else if (proxy.Source == ProxySource.Direct)
        {
            _proxyText = T(Keys.DiagnosticsNetworkProxyDirect);
            _proxyWarningText = string.Empty;
        }
        else
        {
            _proxyText = T(Keys.DiagnosticsNetworkProxyVia, proxy.Url ?? string.Empty, SourceLabel(proxy.Source));
            _proxyWarningText = proxy.Warning ?? string.Empty;
        }

        OnPropertyChanged(nameof(ProxyText));
        OnPropertyChanged(nameof(ProxyWarningText));
        OnPropertyChanged(nameof(HasProxyWarning));
        OnPropertyChanged(nameof(HasNetworkRows));
    }

    private void RebuildStartup()
    {
        StartupRows.Clear();
        double previous = 0;
        for (var index = 0; index < _marks.Count; index++)
        {
            var mark = _marks[index];
            string delta = index == 0
                ? T(Keys.DiagnosticsStartupFirst)
                : T(Keys.DiagnosticsStartupDelta, FormatDuration(TimeSpan.FromMilliseconds(Math.Max(0, mark.ElapsedMilliseconds - previous))));
            StartupRows.Add(new StartupMarkRowViewModel(
                mark.Milestone,
                LabelForMilestone(mark.Milestone),
                FormatDuration(TimeSpan.FromMilliseconds(mark.ElapsedMilliseconds)),
                delta,
                "diagStartup" + Pascalize(mark.Milestone)));
            previous = mark.ElapsedMilliseconds;
        }

        _startupTotalText = _marks.Count == 0
            ? T(Keys.DiagnosticsStartupEmpty)
            : T(Keys.DiagnosticsStartupTotal, FormatDuration(TimeSpan.FromMilliseconds(_marks[^1].ElapsedMilliseconds)));

        OnPropertyChanged(nameof(StartupTotalText));
        OnPropertyChanged(nameof(HasStartupMarks));
    }

    /// <summary>只读探测当前游戏 error.log 的状态；没有选中游戏时显示占位。</summary>
    private void UpdateGameLogStatus()
    {
        var game = AppServices.CurrentGame;
        if (game is null || string.IsNullOrWhiteSpace(game.GameDataDir))
        {
            _gameLogStatusText = T(Keys.DiagnosticsGameLogNoGame);
            return;
        }

        try
        {
            var path = AppServices.FileSystem.Combine(game.GameDataDir, "logs", "error.log");
            var result = LogFileProbe.Probe(AppServices.FileSystem, path, "error.log");
            string state = result.State switch
            {
                LogFileProbeState.Readable => T(Keys.CrashSessionLogReadable),
                LogFileProbeState.Locked => T(Keys.CrashSessionLogLocked),
                LogFileProbeState.Unauthorized => T(Keys.CrashSessionLogUnauthorized),
                LogFileProbeState.ReadOnly => T(Keys.CrashSessionLogReadOnly),
                LogFileProbeState.Missing => T(Keys.CrashSessionLogMissing),
                _ => T(Keys.CrashSessionLogUnknown),
            };
            _gameLogStatusText = T(Keys.DiagnosticsGameLogStatus, result.FileName, state);
        }
        catch (Exception exception)
        {
            AppServices.Logger.Warn("探测游戏日志状态失败：" + exception.Message);
            _gameLogStatusText = T(Keys.CrashSessionLogUnknown);
        }

        OnPropertyChanged(nameof(GameLogStatusText));
    }

    private void UpdateLogTexts()
    {
        var metrics = _logMetrics;
        UpdateGameLogStatus();
        _logSummaryText = metrics.CurrentBytes == 0 && metrics.ArchiveCount == 0
            ? T(Keys.DiagnosticsLogSummaryEmpty)
            : T(
                Keys.DiagnosticsLogSummary,
                SizeFormatter.Format(metrics.CurrentBytes),
                metrics.ArchiveCount,
                SizeFormatter.Format(metrics.TotalBytes));
        _logRotationText = T(
            Keys.DiagnosticsLogRotation,
            SizeFormatter.Format(FileLogger.MaxBytes),
            FileLogger.MaxArchives);
        _logLastWriteText = metrics.LastWriteUtc is { } last
            ? T(Keys.DiagnosticsLogLastWrite, last.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : T(Keys.DiagnosticsLogLastWriteEmpty);

        _logDisplayLines = LogTailReader.ParseDisplayLines(_rawTail, _logFilter, newestFirst: true);
        _logTailEmptyText = _logDisplayLines.Count == 0 ? T(Keys.DiagnosticsLogTailEmpty) : string.Empty;

        OnPropertyChanged(nameof(LogSummaryText));
        OnPropertyChanged(nameof(LogRotationText));
        OnPropertyChanged(nameof(LogLastWriteText));
        OnPropertyChanged(nameof(LogDisplayLines));
        OnPropertyChanged(nameof(LogTailEmptyText));
        OnPropertyChanged(nameof(LogTailTitleText));
        OnPropertyChanged(nameof(HasLogTail));
    }

    /// <summary>仅重建日志显示行（切换过滤档位时走这里，摘要文案不变）。</summary>
    private void RebuildLogDisplayLines()
    {
        _logDisplayLines = LogTailReader.ParseDisplayLines(_rawTail, _logFilter, newestFirst: true);
        _logTailEmptyText = _logDisplayLines.Count == 0 ? T(Keys.DiagnosticsLogTailEmpty) : string.Empty;

        OnPropertyChanged(nameof(LogDisplayLines));
        OnPropertyChanged(nameof(LogTailEmptyText));
        OnPropertyChanged(nameof(HasLogTail));
    }

    private void RefreshHints()
    {
        var settings = AppServices.Settings;
        var rejectedHosts = _hosts.Where(item => item.Value.Rejected > 0).ToArray();
        var busiest = rejectedHosts
            .OrderByDescending(item => item.Value.Rejected)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var input = new PerformanceHintInput(
            WorkingSetBytes: _snapshot?.WorkingSetBytes ?? 0,
            CacheBytes: _cacheBytes,
            WorkshopCacheEntries: _workshopEntries,
            LogBytes: _logMetrics.CurrentBytes,
            LogArchiveCount: _logMetrics.ArchiveCount,
            TranslationEnabled: settings.TranslateEnabled,
            KeepTranslationModelLoaded: settings.KeepTranslationModelLoaded,
            ThrottleRejectedHosts: rejectedHosts.Length,
            BusiestRejectedHost: busiest.Key,
            BusiestRejectedCount: busiest.Value?.Rejected ?? 0,
            StartupElapsedMilliseconds: _marks.Count == 0 ? null : _marks[^1].ElapsedMilliseconds);

        var evaluated = PerformanceHints.Evaluate(input);
        var activeCodes = new HashSet<DiagnosticHintCode>();
        foreach (var hint in evaluated)
        {
            activeCodes.Add(hint.Code);
            var title = T(TitleKeyFor(hint.Code));
            var detail = FormatHintDetail(hint);
            var automationId = "diagHint" + Pascalize(hint.Code.ToString());
            var existing = Hints.FirstOrDefault(row => row.Code == hint.Code);
            if (existing is { } row)
            {
                row.Apply(title, detail);
            }
            else
            {
                Hints.Add(new DiagnosticHintViewModel(hint.Code, title, detail, automationId));
            }
        }

        for (var index = Hints.Count - 1; index >= 0; index--)
        {
            if (!activeCodes.Contains(Hints[index].Code))
            {
                Hints.RemoveAt(index);
            }
        }

        OnPropertyChanged(nameof(HasHints));
    }

    private string FormatHintDetail(DiagnosticHint hint) => hint.Code switch
    {
        DiagnosticHintCode.ThrottleRejected => T(
            Keys.DiagnosticsHintThrottleRejectedDetail,
            hint.Subject ?? T(Keys.CommonUnknown),
            hint.Value.ToString(CultureInfo.InvariantCulture)),
        DiagnosticHintCode.StartupSlow => T(
            Keys.DiagnosticsHintStartupSlowDetail,
            FormatDuration(TimeSpan.FromMilliseconds(hint.Value))),
        DiagnosticHintCode.WorkshopCacheLarge => T(
            Keys.DiagnosticsHintWorkshopCacheLargeDetail,
            hint.Value.ToString(CultureInfo.InvariantCulture)),
        DiagnosticHintCode.TranslationModelResident => T(Keys.DiagnosticsHintTranslationModelResidentDetail),
        DiagnosticHintCode.MemoryHigh or DiagnosticHintCode.MemoryAboveDetailBudget => T(
            DetailKeyFor(hint.Code),
            SizeFormatter.Format(hint.Value)),
        DiagnosticHintCode.CacheLarge => T(Keys.DiagnosticsHintCacheLargeDetail, SizeFormatter.Format(hint.Value)),
        DiagnosticHintCode.LogNearRotation => T(Keys.DiagnosticsHintLogNearRotationDetail, SizeFormatter.Format(hint.Value)),
        DiagnosticHintCode.LogArchives => T(Keys.DiagnosticsHintLogArchivesDetail, SizeFormatter.Format(hint.Value)),
        _ => string.Empty,
    };

    private static string TitleKeyFor(DiagnosticHintCode code) => code switch
    {
        DiagnosticHintCode.MemoryAboveDetailBudget => Keys.DiagnosticsHintMemoryAboveDetailBudgetTitle,
        DiagnosticHintCode.MemoryHigh => Keys.DiagnosticsHintMemoryHighTitle,
        DiagnosticHintCode.ThrottleRejected => Keys.DiagnosticsHintThrottleRejectedTitle,
        DiagnosticHintCode.StartupSlow => Keys.DiagnosticsHintStartupSlowTitle,
        DiagnosticHintCode.CacheLarge => Keys.DiagnosticsHintCacheLargeTitle,
        DiagnosticHintCode.WorkshopCacheLarge => Keys.DiagnosticsHintWorkshopCacheLargeTitle,
        DiagnosticHintCode.LogNearRotation => Keys.DiagnosticsHintLogNearRotationTitle,
        DiagnosticHintCode.LogArchives => Keys.DiagnosticsHintLogArchivesTitle,
        DiagnosticHintCode.TranslationModelResident => Keys.DiagnosticsHintTranslationModelResidentTitle,
        _ => Keys.CommonUnknown,
    };

    private static string DetailKeyFor(DiagnosticHintCode code) => code switch
    {
        DiagnosticHintCode.MemoryAboveDetailBudget => Keys.DiagnosticsHintMemoryAboveDetailBudgetDetail,
        DiagnosticHintCode.MemoryHigh => Keys.DiagnosticsHintMemoryHighDetail,
        _ => Keys.CommonUnknown,
    };

    private static string LabelForMilestone(string milestone) => milestone switch
    {
        StartupTimeline.AppStart => LocalizerKey(Keys.DiagnosticsStartupMarkAppStart),
        StartupTimeline.ServicesReady => LocalizerKey(Keys.DiagnosticsStartupMarkServicesReady),
        StartupTimeline.WindowCreated => LocalizerKey(Keys.DiagnosticsStartupMarkWindowCreated),
        StartupTimeline.ShellReady => LocalizerKey(Keys.DiagnosticsStartupMarkShellReady),
        StartupTimeline.HomeLoaded => LocalizerKey(Keys.DiagnosticsStartupMarkHomeLoaded),
        _ => milestone,
    };

    private static string LocalizerKey(string key) => Localizer.Instance.T(key);

    private static string SourceLabel(ProxySource source) => source switch
    {
        ProxySource.SystemSettings => LocalizerKey(Keys.DiagnosticsNetworkSourceSystem),
        ProxySource.EnvironmentVariable => LocalizerKey(Keys.DiagnosticsNetworkSourceEnvironment),
        ProxySource.Manual => LocalizerKey(Keys.DiagnosticsNetworkSourceManual),
        _ => string.Empty,
    };

    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        if (value.TotalSeconds >= 3600)
        {
            return value.TotalHours.ToString("0", CultureInfo.InvariantCulture) + ":"
                + value.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
                + value.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        if (value.TotalSeconds >= 60)
        {
            return value.Minutes.ToString("0", CultureInfo.InvariantCulture) + ":"
                + value.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        if (value.TotalSeconds >= 1)
        {
            return value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }

        return value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
    }

    private static string Pascalize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        bool upper = true;
        foreach (char ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(ch) : ch);
            upper = false;
        }

        return builder.ToString();
    }
}
