namespace PdxLauncher.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Storage;

/// <summary>
/// AppSettings 的读写。正式落点是自有库 <c>pdxnext.sqlite</c> 的 <c>settings</c> 表（键 <c>settings</c>）；
/// 旧的 <c>data\settings.json</c> 只在导入/自检路径上使用（见 <see cref="IDocumentFile"/>）。
/// 旧位置 %LOCALAPPDATA%\PdxNext 只由 <see cref="DataRootMigrator"/> 读取一次用于迁移。
/// 读取容错：文档缺失/损坏一律返回默认值并记日志，绝不抛异常。
/// <para>
/// 写入分两条路：<see cref="Save"/> 立即落盘并返回结果（关闭窗口、改代理这类必须确定生效的场合），
/// <see cref="SaveDeferred"/> 只登记「待写」并在 <see cref="DeferredSaveDelayMilliseconds"/> 后合并成一次写
/// （设置页连续勾选/拖动时避免每次都整份 JSON 落盘）。两条路都会取消对方尚未执行的写，
/// 关窗口前调用 <see cref="Flush"/> 可确保最后一次改动不会丢。
/// </para>
/// </summary>
public sealed class AppSettingsStore : IDisposable
{
    /// <summary>延迟保存的合并窗口；期间再次 <see cref="SaveDeferred"/> 会重置计时。</summary>
    public const int DeferredSaveDelayMilliseconds = 300;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IDocumentFile _document;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly Timer _deferredTimer;

    /// <summary>待写入的设置对象。这里存的是引用而不是副本：AppServices.Settings 是长期复用的同一实例，
    /// 延迟到期时序列化它就能拿到最新值，不需要在登记时做一次深拷贝。</summary>
    private AppSettings? _pending;

    private bool _disposed;

    /// <summary>自检/兼容用：设置存在一个 JSON 文件里（旧布局）。</summary>
    public AppSettingsStore(IFileSystem fs, IAppLogger logger, string settingsPath)
        : this(new FileDocumentFile(fs, settingsPath), logger)
    {
    }

    /// <summary>正式路径：设置作为一份文档存在自有库里。</summary>
    public AppSettingsStore(IJsonDocumentStore documents, IAppLogger logger)
        : this(new StoreDocumentFile(documents, JsonDocumentKeys.Settings), logger)
    {
    }

    /// <summary>直接给一个文档落点（库或文件）。</summary>
    public AppSettingsStore(IDocumentFile document, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(logger);

        _document = document;
        _logger = logger;
        _deferredTimer = new Timer(OnDeferredTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>当前设置的落点（<c>db:settings</c> 或文件绝对路径），排障用。</summary>
    public string SettingsPath => _document.DisplayPath;

    public static string DefaultPath => Path.Combine(PdxDataRoot.Directory, "settings.json");

    /// <summary>是否还有尚未落盘的延迟写（自检与关闭流程用来判断要不要 Flush）。</summary>
    public bool HasPendingWrite
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                var json = _document.ReadText();
                if (json is null)
                {
                    return new AppSettings();
                }

                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
            catch (JsonException ex)
            {
                _logger.Warn("settings.json 解析失败，使用默认设置: " + ex.Message);
                return new AppSettings();
            }
            catch (IOException ex)
            {
                _logger.Warn("settings.json 读取失败，使用默认设置: " + ex.Message);
                return new AppSettings();
            }
        }
    }

    /// <summary>立即写盘。成功返回 <see cref="AppResult{T}.Ok"/>；失败返回 <see cref="ErrorKind.Io"/>，
    /// 调用方可以据此在界面上提示，而不是像以前那样写失败也当作成功。</summary>
    public AppResult<bool> Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            // 立即写覆盖掉尚未执行的延迟写：此刻的 settings 已经包含那次延迟写的全部内容。
            _pending = null;
            _deferredTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return WriteLocked(settings);
        }
    }

    /// <summary>登记一次延迟写，<see cref="DeferredSaveDelayMilliseconds"/> 后落盘。
    /// 期间多次调用只会保留最后一次登记、只落盘一次。</summary>
    public void SaveDeferred(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = settings;
            _deferredTimer.Change(DeferredSaveDelayMilliseconds, Timeout.Infinite);
        }
    }

    /// <summary>把尚未执行的延迟写立刻落盘。没有待写内容时返回成功（幂等）。</summary>
    public AppResult<bool> Flush()
    {
        lock (_gate)
        {
            var pending = _pending;
            _pending = null;
            _deferredTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return pending is null ? AppResult<bool>.Ok(true) : WriteLocked(pending);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var pending = _pending;
            _pending = null;
            _deferredTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (pending is not null)
            {
                WriteLocked(pending);
            }
        }

        _deferredTimer.Dispose();
    }

    private void OnDeferredTimerElapsed(object? state)
    {
        AppSettings? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
            if (pending is null)
            {
                return;
            }
        }

        // 定时器回调里不抛异常：写失败只能记日志，不能把异常丢到线程池上把进程带走。
        var result = Save(pending);
        if (!result.IsSuccess)
        {
            _logger.Error("设置延迟保存失败: " + result.ErrorMessage);
        }
    }

    /// <summary>调用方必须已持有 <see cref="_gate"/>。</summary>
    private AppResult<bool> WriteLocked(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, Options);
            if (!_document.WriteText(json))
            {
                _logger.Error("settings 写入失败（落点=" + _document.DisplayPath + "）");
                return AppResult<bool>.Fail(ErrorKind.Io, "settings-write:" + _document.DisplayPath);
            }

            return AppResult<bool>.Ok(true);
        }
        catch (IOException ex)
        {
            _logger.Error("settings.json 写入失败", ex);
            return AppResult<bool>.Fail(ErrorKind.Io, "settings-write:" + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error("settings.json 写入失败（无权限）", ex);
            return AppResult<bool>.Fail(ErrorKind.Io, "settings-write-denied:" + ex.Message);
        }
        catch (Exception ex)
        {
            // 序列化失败、目录创建失败等：同样不能把异常抛给 UI。
            _logger.Error("settings.json 写入失败（未分类）", ex);
            return AppResult<bool>.Fail(ErrorKind.Io, "settings-write-failed:" + ex.Message);
        }
    }
}
