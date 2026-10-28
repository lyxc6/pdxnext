namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Parsing;

/// <summary>
/// 从 Steam 的 <c>steamapps/workshop/appworkshop_&lt;appid&gt;.acf</c> 读某个创意工坊条目
/// 在本机的订阅 / 安装状态。
/// <para>
/// 这是**不依赖 Steamworks SDK 的回退路径**：SDK 不可用（Steam 没开、没找到 dll、
/// 用户没开实验开关）时，UI 至少还能显示"已订阅/已安装"。
/// 读不到一律返回 <see cref="WorkshopInstallState.Unknown"/>，绝不抛异常。
/// </para>
/// </summary>
public sealed class WorkshopSubscriptionReader
{
    private const string WorkshopFolder = "workshop";

    private readonly IFileSystem _fileSystem;
    private readonly SteamLibraryLocator _libraries;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    public WorkshopSubscriptionReader(IFileSystem fileSystem, SteamLibraryLocator libraries, IAppLogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 读取状态。参数非法、找不到 acf、acf 畸形都返回 <see cref="WorkshopInstallState.Unknown"/>。
    /// </summary>
    /// <param name="appId">游戏的 Steam appid（创意工坊所属）。</param>
    /// <param name="publishedFileId">创意工坊条目 id。</param>
    public WorkshopInstallState Read(int appId, string? publishedFileId)
    {
        if (appId <= 0 || string.IsNullOrWhiteSpace(publishedFileId))
        {
            return WorkshopInstallState.Unknown;
        }

        var fileId = publishedFileId!.Trim();
        IReadOnlyList<string> libraries;
        try
        {
            libraries = _libraries.FindLibraries();
        }
        catch (Exception ex)
        {
            _logger.Warn("读取 Steam 库列表失败，创意工坊订阅状态未知：" + ex.Message);
            return WorkshopInstallState.Unknown;
        }

        string? acfPath = null;
        var contentInstalled = false;
        foreach (var library in libraries)
        {
            if (acfPath is null)
            {
                var candidate = AcfPath(library, appId);
                if (_fileSystem.FileExists(candidate))
                {
                    acfPath = candidate;
                }
            }

            if (!contentInstalled)
            {
                var content = _fileSystem.Combine(library, "steamapps", WorkshopFolder, "content",
                    appId.ToString(System.Globalization.CultureInfo.InvariantCulture), fileId);
                contentInstalled = _fileSystem.DirectoryExists(content);
            }
        }

        if (acfPath is null)
        {
            // 没有 acf：只能凭内容目录判断"装过"，订阅状态保持未知。
            return new WorkshopInstallState(
                IsSubscribed: null,
                IsInstalled: contentInstalled ? true : null,
                IsDownloading: null,
                AcfPath: null);
        }

        try
        {
            return ReadFromAcf(_fileSystem, _logger, acfPath, fileId, contentInstalled);
        }
        catch (Exception ex)
        {
            _logger.Warn("解析创意工坊订阅清单失败：" + acfPath + "：" + ex.Message);
            return WorkshopInstallState.Unknown;
        }
    }

    /// <summary>
    /// 纯解析入口：给一个 acf 路径与「内容目录是否已存在」，返回订阅 / 安装状态。
    /// 抽成静态方法是为了让自检能在临时目录里造 acf，不必依赖本机真的装了 Steam。
    /// </summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="logger">日志。</param>
    /// <param name="acfPath">appworkshop_&lt;appid&gt;.acf 的完整路径。</param>
    /// <param name="publishedFileId">创意工坊条目 id（已 Trim）。</param>
    /// <param name="contentInstalled">对应内容目录是否存在。</param>
    public static WorkshopInstallState ReadFromAcf(
        IFileSystem fileSystem,
        IAppLogger logger,
        string acfPath,
        string publishedFileId,
        bool contentInstalled)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(acfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);

        try
        {
            var root = ValveKeyValuesParser.ParseFile(fileSystem, acfPath);
            var workshop = root.FindChild("AppWorkshop");
            if (workshop is null || string.IsNullOrWhiteSpace(workshop.GetValue("appid")))
            {
                // 解析器对残缺文件是宽松的：坏 acf 会解析出空块而不是抛异常。
                // 真实的 appworkshop_*.acf 一定有 appid；缺了就当成「未知」，
                // 否则界面会把损坏的清单显示成「你还没订阅」。
                logger.Warn("创意工坊订阅清单无法识别（缺 AppWorkshop 或 appid），按未知处理：" + acfPath);
                return WorkshopInstallState.Unknown;
            }

            var subscribed = workshop.FindChild("WorkshopItemDetails")?.FindChild(publishedFileId) is not null;
            var installedEntry = workshop.FindChild("WorkshopItemsInstalled")?.FindChild(publishedFileId) is not null;
            var installed = installedEntry || contentInstalled;

            bool? downloading = subscribed
                ? (!installed ? true : false)
                : null;

            return new WorkshopInstallState(subscribed, installed, downloading, acfPath);
        }
        catch (Exception ex)
        {
            logger.Warn("解析创意工坊订阅清单失败：" + acfPath + "：" + ex.Message);
            return WorkshopInstallState.Unknown;
        }
    }

    private string AcfPath(string libraryRoot, int appId) => _fileSystem.Combine(
        libraryRoot,
        "steamapps",
        WorkshopFolder,
        "appworkshop_" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".acf");
}
