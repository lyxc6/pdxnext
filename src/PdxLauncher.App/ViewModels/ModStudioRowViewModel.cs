namespace PdxLauncher.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Playsets;

/// <summary>「Mod 创建」页左侧列表的一行：本地 mod、描述符与发布记录。</summary>
public sealed partial class ModStudioRowViewModel : LocalizedObservableObject
{
    private readonly bool _canPublishGame;

    /// <summary>构造一行。</summary>
    /// <param name="entry">合并后的条目。</param>
    /// <param name="canPublishGame">当前游戏是否有 Steam 创意工坊。</param>
    /// <param name="gameVersion">当前游戏版本，用于判断支持版本；未知时 null。</param>
    /// <param name="thumbnailSize">缩略图边长。</param>
    public ModStudioRowViewModel(
        ModStudioEntry entry,
        bool canPublishGame,
        string? gameVersion = null,
        int thumbnailSize = 40)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        ModId = entry.Mod.ModId;
        Name = entry.Mod.Name;
        DirPath = entry.Mod.DirPath;
        ArchivePath = entry.Mod.ArchivePath;
        ThumbnailPath = entry.Descriptor?.PicturePath ?? entry.Mod.ThumbnailPath;
        ThumbnailSize = thumbnailSize;
        _canPublishGame = canPublishGame;
        GameVersion = gameVersion;
    }

    /// <summary>原始条目。</summary>
    public ModStudioEntry Entry { get; }

    /// <summary>mod 主键。</summary>
    public Guid ModId { get; }

    /// <summary>展示名。</summary>
    public string Name { get; }

    /// <summary>本地目录；压缩包型 mod 为 null。</summary>
    public string? DirPath { get; }

    /// <summary>压缩包路径；目录型 mod 为 null。</summary>
    public string? ArchivePath { get; }

    /// <summary>封面路径。</summary>
    public string? ThumbnailPath { get; }

    /// <summary>是否有封面。</summary>
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);

    /// <summary>封面边长。</summary>
    public double ThumbnailSize { get; }

    /// <summary>描述符；没有读到为 null。</summary>
    public ModDescriptorInfo? Descriptor => Entry.Descriptor;

    /// <summary>发布记录；未上传过为 null。</summary>
    public ModPublication? Publication => Entry.Publication;

    /// <summary>当前游戏版本。</summary>
    public string? GameVersion { get; }

    /// <summary>mod 自己声明的版本。</summary>
    public string VersionText => Descriptor?.Version ?? string.Empty;

    /// <summary>mod 声明的支持版本。</summary>
    public string SupportedVersionText => Descriptor?.SupportedVersion ?? Entry.Mod.RequiredVersion;

    /// <summary>标签文案。</summary>
    public string TagsText => string.Join(", ", Descriptor?.Tags ?? Array.Empty<string>());

    /// <summary>描述符家族文案。</summary>
    public string DescriptorText => Descriptor is { Family: not ModDescriptorFamily.None }
        ? T(ModDescriptorRules.DescribeKey(Descriptor.Family))
        : T(Keys.ModstudioDescriptorUnsupported);

    /// <summary>大小文案。</summary>
    public string SizeText => ModSizeText.Format(Entry.Mod.SizeBytes);

    /// <summary>来源文案（本页只列本地 mod）。</summary>
    public string OriginText => T(Keys.CommonSourceLocal);

    /// <summary>
    /// 发布状态文案，四态：未发布 / 发布未完成 / 已发布（未公开）/ 已发布。
    /// <para>
    /// 「发布未完成」指上次提交失败但已经拿到 id；「未公开」指 Steam 侧事实可见性不是公开
    /// （仅好友 / 私密 / 未列出）——作者最容易把这一档误认为「没传上去」。
    /// </para>
    /// </summary>
    public string PublicationText
    {
        get
        {
            if (Publication is not { PublishedFileId: { } id })
            {
                return T(Keys.ModstudioRowUnpublished);
            }

            if (Publication.LastError is not null)
            {
                return T(Keys.ModstudioRowPublishFailed, id);
            }

            return Publication.IsUnlisted
                ? T(Keys.ModstudioRowPublishedUnlisted, id)
                : T(Keys.ModstudioRowPublished, id);
        }
    }

    /// <summary>是否已经发布过。</summary>
    public bool HasPublication => Publication?.PublishedFileId is not null;

    /// <summary>
    /// 列表行徽标的短文案：只说「已发布」，不带工坊 id。
    /// <para>
    /// 带 <c>{0}</c> 的文案不能裸绑 <c>{x:Bind L.Xxx}</c>（Localizer 无参时返回模板原文），
    /// 而 id 已经由 <see cref="SubtitleText"/> 与详情面板显示，徽标再带一次就是重复。
    /// </para>
    /// </summary>
    public string PublicationBadgeText => HasPublication ? T(Keys.ModstudioRowPublishedShort) : string.Empty;

    /// <summary>创意工坊 id 文本。</summary>
    public string? WorkshopId => Publication?.PublishedFileId?.ToString();

    /// <summary>是否有可打开的工坊页面。</summary>
    public bool CanOpenWorkshop => Publication?.PublishedFileId is not null;

    /// <summary>是否有可编辑的描述符文件（读到了家族且文件存在）。</summary>
    public bool CanEditDescriptor => Descriptor is { Family: not ModDescriptorFamily.None, DescriptorPath.Length: > 0 };

    /// <summary>上方辅助行：大小 + 发布状态。</summary>
    public string SubtitleText
    {
        get
        {
            var size = SizeText;
            return string.IsNullOrWhiteSpace(size)
                ? PublicationText
                : size + " · " + PublicationText;
        }
    }

    /// <summary>能否上传这个 mod。</summary>
    public bool CanUpload
        => _canPublishGame
            && Descriptor is { Family: not ModDescriptorFamily.None }
            && !string.IsNullOrWhiteSpace(DirPath)
            && string.IsNullOrWhiteSpace(ArchivePath);

    /// <summary>不能上传的原因文案；能上传时为空串。</summary>
    public string UploadDisabledText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DirPath))
            {
                return T(Keys.ModstudioUploadNotLocal);
            }

            if (!string.IsNullOrWhiteSpace(ArchivePath))
            {
                return T(Keys.ModstudioUploadArchived);
            }

            if (Descriptor is not { Family: not ModDescriptorFamily.None })
            {
                return T(Keys.ModstudioDescriptorUnsupported);
            }

            return _canPublishGame ? string.Empty : T(Keys.ModstudioNoWorkshopHint);
        }
    }

    /// <summary>行的自动化 Id。</summary>
    public string RowAutomationId => "modStudioRow_" + ModId.ToString("N");
}
