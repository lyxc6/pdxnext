namespace PdxLauncher.Core.Online.Translation.Local;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation;

/// <summary>
/// 本地翻译的运行时：把术语词典、模型清单、模型仓库与两个本地引擎收在一处。
/// <para>
/// 应用层只跟这个类打交道：问它要引擎列表、要下载模型、要读写术语表。
/// </para>
/// </summary>
public sealed class LocalTranslationRuntime : IDisposable
{
    private readonly IAppLogger _logger;
    private BergamotTranslationEngine? _bergamot;
    private IndexTranslateEngine? _indexTranslate;
    private bool _disposed;

    /// <summary>创建运行时。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="http">HTTP 抓取器（下载模型用）。</param>
    public LocalTranslationRuntime(IFileSystem fileSystem, IAppLogger logger, IHttpFetcher http)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(http);

        _logger = logger;
        Glossary = new LocalTranslationGlossary(fileSystem);
        Catalog = LocalTranslationModelCatalog.CreateDefault();
        Store = new LocalTranslationModelStore(Catalog);
        Downloader = new LocalTranslationModelDownloader(http, logger, Catalog, Store);
    }

    /// <summary>术语词典。</summary>
    public LocalTranslationGlossary Glossary { get; }

    /// <summary>模型清单。</summary>
    public LocalTranslationModelCatalog Catalog { get; }

    /// <summary>模型仓库（判断是否已安装、删除模型）。</summary>
    public LocalTranslationModelStore Store { get; }

    /// <summary>模型下载器。</summary>
    public LocalTranslationModelDownloader Downloader { get; }

    /// <summary>
    /// 创建本地引擎；顺序即回落优先级（Index 质量更好，Bergamot 更快更小）。
    /// </summary>
    public IReadOnlyList<ITranslationEngine> CreateEngines()
    {
        var engines = new List<ITranslationEngine>(2);

        _indexTranslate ??= new IndexTranslateEngine(_logger, Glossary, Store);
        engines.Add(_indexTranslate);

        _bergamot ??= new BergamotTranslationEngine(_logger, Glossary, Store);
        engines.Add(_bergamot);

        return engines;
    }

    /// <summary>下载并安装模型。</summary>
    public Task<LocalTranslationModelDownloadResult> DownloadAsync(
        LocalTranslationModelKind kind,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Downloader.DownloadAsync(kind, progress, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bergamot?.Dispose();
        _bergamot = null;
        _indexTranslate?.Dispose();
        _indexTranslate = null;
    }
}
