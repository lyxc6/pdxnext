namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Storage;

/// <summary>
/// 设置页「缓存」卡片里的一行：标题、说明、落盘路径、当前占用与清理进度。
/// 只描述一类缓存；清除动作在构造时注入，使用方不再按 <see cref="Key"/> 分派。
/// </summary>
public sealed class CacheRowViewModel : LocalizedObservableObject
{
    /// <summary>主题素材缓存（theme-cache 目录）。</summary>
    public const string ThemeKey = "theme";

    /// <summary>官方公告缓存（news-cache 目录 + 公告配图）。</summary>
    public const string NewsKey = "news";

    /// <summary>Mod 缩略图缓存（mod-cache 目录）。</summary>
    public const string ModThumbnailsKey = "modThumbnails";

    /// <summary>DLC 简介缓存（dlc-descriptions.json）。</summary>
    public const string DlcDescriptionsKey = "dlcDescriptions";

    /// <summary>DDS 缩略图转码缓存（image-cache 目录）。</summary>
    public const string ImagesKey = "images";

    /// <summary>创意工坊 mod 信息缓存（workshop-info 目录：索引 + 封面）。</summary>
    public const string WorkshopKey = "workshopInfo";

    private readonly string _titleKey;
    private readonly string _descriptionKey;
    private readonly Action _clear;
    private CacheUsage _usage = CacheUsage.Empty;
    private bool _isBusy;

    public CacheRowViewModel(
        string key,
        string titleKey,
        string descriptionKey,
        string path,
        Action clear,
        string? documentKey = null,
        string? folderPath = null)
    {
        Key = key;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        Path = path;
        _clear = clear;
        DocumentKey = documentKey;
        FolderPath = folderPath;
    }

    /// <summary>缓存类别标识，见本类的 *Key 常量。</summary>
    public string Key { get; }

    /// <summary>缓存落盘路径（库键或磁盘文件/目录），只用于展示。</summary>
    public string Path { get; }

    /// <summary>自有库里这份索引的键（没有入库的缓存为 null）。</summary>
    public string? DocumentKey { get; }

    /// <summary>仍在磁盘上的图片目录（索引入库后，图片仍按原布局存放）。</summary>
    public string? FolderPath { get; }

    /// <summary>最近一次统计到的占用。</summary>
    public CacheUsage Usage => _usage;

    /// <summary>类别名，例如「主题素材」。</summary>
    public string Title => T(_titleKey);

    /// <summary>一句话说明，含过期策略与清理后的影响。</summary>
    public string Description => T(_descriptionKey);

    /// <summary>占用文案，例如「12.3 MB，30 个文件」；空缓存显示「暂无缓存」。</summary>
    public string SizeText => _usage.Files <= 0
        ? T(Keys.SettingsCacheEmpty)
        : T(Keys.SettingsCacheSize, SizeFormatter.Format(_usage.Bytes), _usage.Files);

    /// <summary>正在清理（按钮禁用、文案换成「处理中」）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanClear));
            }
        }
    }

    /// <summary>没有正在进行的清理时可以点「清除」。</summary>
    public bool CanClear => !_isBusy;

    /// <summary>统计完后回填占用并刷新文案。</summary>
    public void UpdateUsage(CacheUsage usage)
    {
        _usage = usage;
        OnPropertyChanged(nameof(Usage));
        OnPropertyChanged(nameof(SizeText));
    }

    /// <summary>清理开始 / 结束时切换忙碌状态。</summary>
    public void SetBusy(bool busy) => IsBusy = busy;

    /// <summary>执行本行缓存的清除（清什么由构造时注入的委托决定）。</summary>
    public void Clear() => _clear();
}
