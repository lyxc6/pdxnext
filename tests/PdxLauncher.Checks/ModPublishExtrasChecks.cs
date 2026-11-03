namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Steam;

/// <summary>
/// 创意工坊发布扩展（可见性四档 / 内容描述词 / 多图 + YouTube 视频 / 多语言 / 内置封面）的自检。
/// <para>
/// 这些内容全是「传错了 Steam 不报错、只是悄悄不生效」的类型：可见性越界会退化成公开，
/// 多语言提交顺序写错会覆盖主标题，封面缺失会变成 1×1 透明图。所以逐条钉死。
/// </para>
/// <para>纯本地用例，不联网、不调 Steamworks。</para>
/// </summary>
public static class ModPublishExtrasChecks
{
    /// <summary>运行本组检查。</summary>
    /// <param name="fileSystem">文件系统（封面对比要用）。</param>
    /// <returns>0 表示全部通过。</returns>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        try
        {
            CheckVisibilityMapping();
            CheckPreviewLimits();
            CheckYouTubeIds();
            CheckLanguages();
            CheckRequestRoundTrip();
            CheckLegacyPublication();
            CheckDefaultCover(fileSystem);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("发布扩展：未抛出异常", false, ex.Message);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckVisibilityMapping()
    {
        // 数值必须和 SDK 的 ERemoteStoragePublishedFileVisibility 对齐，否则 Steam 侧会拒或不生效。
        ChecksContext.Equal("可见性：公开=0", 0, (int)SteamWorkshopVisibility.Public);
        ChecksContext.Equal("可见性：仅好友=1", 1, (int)SteamWorkshopVisibility.FriendsOnly);
        ChecksContext.Equal("可见性：隐藏=2", 2, (int)SteamWorkshopVisibility.Private);
        ChecksContext.Equal("可见性：非公开=3", 3, (int)SteamWorkshopVisibility.Unlisted);

        ChecksContext.Equal("可见性：共四档", 4, SteamWorkshopVisibilityExtensions.All.Count);

        ChecksContext.Equal(
            "可见性：ToNative 与枚举值一致",
            (int)SteamWorkshopVisibility.Unlisted,
            SteamWorkshopVisibility.Unlisted.ToNative());

        ChecksContext.Equal(
            "可见性：FromNative 还原非公开",
            SteamWorkshopVisibility.Unlisted,
            SteamWorkshopVisibilityExtensions.FromNative(3));

        ChecksContext.Equal(
            "可见性：越界回落公开",
            SteamWorkshopVisibility.Public,
            SteamWorkshopVisibilityExtensions.FromNative(99));

        // 旧记录只有 bool：true 早先写 0、false 早先写 2，这里必须保持同样语义。
        ChecksContext.Equal(
            "可见性：旧 bool=true 等价公开",
            SteamWorkshopVisibility.Public,
            SteamWorkshopVisibilityExtensions.Effective(true));
        ChecksContext.Equal(
            "可见性：旧 bool=false 等价隐藏",
            SteamWorkshopVisibility.Private,
            SteamWorkshopVisibilityExtensions.Effective(false));
    }

    private static void CheckPreviewLimits()
    {
        ChecksContext.Check(
            "预览图：0 字节被拒",
            !ModPublishLimits.IsPreviewFileAcceptable(0, out var emptyError)
            && !string.IsNullOrWhiteSpace(emptyError));

        ChecksContext.Check(
            "预览图：刚好 1 MB 被拒",
            !ModPublishLimits.IsPreviewFileAcceptable(ModPublishLimits.MaxPreviewBytes, out _));

        ChecksContext.Check(
            "预览图：1 MB 差 1 字节可接受",
            ModPublishLimits.IsPreviewFileAcceptable(ModPublishLimits.MaxPreviewBytes - 1, out var okError)
            && okError is null);

        ChecksContext.Equal("预览图：单次最多 8 张", 8, ModPublishLimits.MaxAdditionalPreviews);
        ChecksContext.Equal("多语言：最多 12 种", 12, ModPublishLimits.MaxLocalizations);
    }

    private static void CheckYouTubeIds()
    {
        // Steam 侧只存 11 位 video id，把整条链接塞进去会变成坏预览。
        var accepted = new (string Input, string Expected)[]
        {
            ("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ"),
            ("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
            ("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
            ("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
            ("dQw4w9WgXcQ", "dQw4w9WgXcQ"),
        };

        foreach (var (input, expected) in accepted)
        {
            ChecksContext.Check(
                "YouTube：可解析 " + input,
                YouTubeVideoId.TryParse(input, out var id) && id == expected);
        }

        var rejected = new[]
        {
            string.Empty,
            "   ",
            "https://vimeo.com/12345",
            "https://example.com/watch?v=dQw4w9WgXcQ",
            "https://www.youtube.com/watch?v=short",
        };

        foreach (var input in rejected)
        {
            ChecksContext.Check(
                "YouTube：拒绝 '" + input + "'",
                !YouTubeVideoId.TryParse(input, out _));
        }
    }

    private static void CheckLanguages()
    {
        ChecksContext.Check("语言表：至少 20 种", SteamWorkshopLanguages.All.Count >= 20);
        ChecksContext.Equal("语言表：默认英语", "english", SteamWorkshopLanguages.Default);
        ChecksContext.Equal("语言表：简中码", "schinese", SteamWorkshopLanguages.SimplifiedChinese);

        ChecksContext.Check(
            "语言表：简中在内",
            SteamWorkshopLanguages.All.Any(static item => item.Code == SteamWorkshopLanguages.SimplifiedChinese));

        ChecksContext.Check(
            "语言表：代码唯一",
            SteamWorkshopLanguages.All.Select(static item => item.Code).Distinct(StringComparer.Ordinal).Count()
            == SteamWorkshopLanguages.All.Count);

        ChecksContext.Equal(
            "语言表：未知码归一为英语",
            SteamWorkshopLanguages.Default,
            SteamWorkshopLanguages.Normalize("klingon"));

        ChecksContext.Equal(
            "语言表：已知码原样保留",
            SteamWorkshopLanguages.SimplifiedChinese,
            SteamWorkshopLanguages.Normalize(SteamWorkshopLanguages.SimplifiedChinese));

        ChecksContext.Check(
            "语言表：显示名不为空",
            SteamWorkshopLanguages.All.All(static item => !string.IsNullOrWhiteSpace(item.DisplayName)));

        // 上传页「添加语言」下拉：空集合挑第一个，已用过的跳过，全占用返回 null。
        ChecksContext.Equal(
            "语言表：空集合挑第一个未用语言",
            SteamWorkshopLanguages.SimplifiedChinese,
            SteamWorkshopLanguages.PickUnused(Array.Empty<string>()));

        ChecksContext.Equal(
            "语言表：跳过已用语言",
            SteamWorkshopLanguages.All[1].Code,
            SteamWorkshopLanguages.PickUnused([SteamWorkshopLanguages.SimplifiedChinese]));

        ChecksContext.Check(
            "语言表：全部占用时返回 null",
            SteamWorkshopLanguages.PickUnused(SteamWorkshopLanguages.All.Select(static item => item.Code))
            is null);

        ChecksContext.Check(
            "语言表：判重忽略大小写",
            SteamWorkshopLanguages.IsDuplicate([SteamWorkshopLanguages.Default], "ENGLISH"));

        ChecksContext.Check(
            "语言表：未知码归一后判重",
            SteamWorkshopLanguages.IsDuplicate([SteamWorkshopLanguages.Default], "klingon"));

        ChecksContext.Check(
            "语言表：不同语言不判重",
            !SteamWorkshopLanguages.IsDuplicate(
                [SteamWorkshopLanguages.SimplifiedChinese],
                SteamWorkshopLanguages.Default));
    }

    private static void CheckRequestRoundTrip()
    {
        var request = new ModPublishRequest(
            AppId: 1158310,
            PublishedFileId: 42,
            ContentPath: @"C:\mods\My Mod",
            PreviewPath: @"C:\mods\cover.png",
            Title: "虚空造物",
            Description: "描述",
            ChangeNote: "note",
            Tags: ["Gameplay"],
            Visibility: SteamWorkshopVisibility.FriendsOnly,
            ContentDescriptors: [1, 5],
            PreviewPaths: [@"C:\mods\a.png", @"C:\mods\b.png"],
            PreviewVideoIds: ["dQw4w9WgXcQ"],
            Localizations: [new ModLocalizedText("schinese", "标题", "描述")]);

        var line = SteamHostProtocol.Encode(request);
        ChecksContext.Check(
            "发布请求：能解回来",
            SteamHostProtocol.TryDecode<ModPublishRequest>(line, out var decoded));

        ChecksContext.Equal("发布请求：可见性往返", SteamWorkshopVisibility.FriendsOnly, decoded!.Visibility);
        ChecksContext.Equal("发布请求：描述词往返", 2, decoded.ContentDescriptors!.Count);
        ChecksContext.Equal("发布请求：描述词顺序往返", 5, decoded.ContentDescriptors![1]);
        ChecksContext.Equal("发布请求：附加图往返", 2, decoded.PreviewPaths!.Count);
        ChecksContext.Equal("发布请求：视频往返", "dQw4w9WgXcQ", decoded.PreviewVideoIds![0]);
        ChecksContext.Equal("发布请求：语言往返", "schinese", decoded.Localizations![0].Language);
        ChecksContext.Equal("发布请求：语言标题往返", "标题", decoded.Localizations![0].Title);

        // 老客户端只知道可见性，扩展字段必须允许缺省。
        var minimal = new ModPublishRequest(
            AppId: 1158310,
            PublishedFileId: null,
            ContentPath: @"C:\mods\x",
            PreviewPath: null,
            Title: "t",
            Description: "d",
            ChangeNote: null,
            Tags: [],
            Visibility: SteamWorkshopVisibility.Public);

        ChecksContext.Check(
            "发布请求：扩展字段可缺省",
            SteamHostProtocol.TryDecode<ModPublishRequest>(SteamHostProtocol.Encode(minimal), out var plain)
            && plain!.ContentDescriptors is null
            && plain.PreviewPaths is null
            && plain.PreviewVideoIds is null
            && plain.Localizations is null);
    }

    private static void CheckLegacyPublication()
    {
        // 升级前的记录只有 Public 布尔；EffectiveVisibility 必须兼容出正确档位。
        var legacy = new ModPublication(
            "victoria3",
            @"C:\mods\My Mod",
            7UL,
            "Title",
            "Description",
            ["Gameplay"],
            Public: false,
            PreviewPath: null,
            LastError: null,
            UpdatedAt: DateTimeOffset.UnixEpoch);

        ChecksContext.Equal(
            "旧记录：false 读作隐藏",
            SteamWorkshopVisibility.Private,
            legacy.EffectiveVisibility);
        ChecksContext.Equal("旧记录：扩展图默认为空", 0, legacy.ExtraPreviews.Count);
        ChecksContext.Equal("旧记录：视频默认为空", 0, legacy.Videos.Count);
        ChecksContext.Equal("旧记录：多语言默认为空", 0, legacy.ExtraLocalizations.Count);
        ChecksContext.Equal("旧记录：描述词默认为空", 0, legacy.Descriptors.Count);

        var modern = legacy with { Visibility = SteamWorkshopVisibility.Unlisted };
        ChecksContext.Equal(
            "新记录：显式可见性优先于 bool",
            SteamWorkshopVisibility.Unlisted,
            modern.EffectiveVisibility);
    }

    private static void CheckDefaultCover(IFileSystem fs)
    {
        var bytes = ModCoverAssets.Default;
        ChecksContext.Check("默认封面：非空", bytes.Length > 128);

        // PNG 签名：Steam 只认图片，写错格式等于没封面。
        ChecksContext.Check(
            "默认封面：是 PNG",
            bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A);

        ChecksContext.Check(
            "默认封面：小于 1 MB 上限",
            bytes.Length < ModPublishLimits.MaxPreviewBytes);

        var root = PlaysetChecks.CreateTempRoot(fs, "modcover");
        try
        {
            var target = fs.Combine(root, "mod", "thumb.png");
            ChecksContext.Check("默认封面：写入成功", ModCoverAssets.WriteDefault(fs, target));
            ChecksContext.Check("默认封面：文件存在", fs.FileExists(target));
            ChecksContext.Check(
                "默认封面：字节一致",
                fs.FileExists(target) && fs.ReadAllBytes(target).SequenceEqual(bytes));

            // 新建 mod 时没选封面，写出来的必须是内置封面而不是 1×1 占位。
            var game = new GameInstallation(
                GameId: "victoria3",
                DisplayName: "Victoria 3",
                Version: "test",
                InstallRoot: root,
                LauncherSettingsPath: fs.Combine(root, "launcher-settings.json"),
                Format: LoadFileFormat.ContentLoadV1,
                GameDataDir: fs.Combine(root, "game"),
                DlcPath: fs.Combine(root, "dlc"),
                ModDir: fs.Combine(root, "mod"),
                ExePath: fs.Combine(root, "binaries", "victoria3.exe"),
                ExeArgs: Array.Empty<string>(),
                Alternatives: Array.Empty<AlternativeExecutable>(),
                Theme: new ThemeSpec(
                    Background: null,
                    Logo: null,
                    AppIcon: null,
                    AppTaskbarIcon: null,
                    ButtonBackground: null,
                    ButtonFont: null,
                    ButtonTextColor: "#FFFFFF",
                    ButtonHoverTextColor: "#FFFFFF",
                    FontSize: 14,
                    FontWeight: 400,
                    TextShadow: null,
                    Source: ThemeAssetSource.BuiltInPlaceholder),
                BrowserDlcUrl: null,
                BrowserModUrl: null,
                SteamAppId: 529340,
                Support: GameSupport.Full);
            fs.EnsureDirectory(game.ModDir);

            var result = ModCreator.Create(
                game,
                new ModCreateRequest("虚空造物+货运红利", "my_mod", "1.0", "1.9", ["Gameplay"], null, null),
                fs);
            ChecksContext.Check("新建 mod：成功", result.Ok, (string?)result.ErrorCode ?? string.Empty);

            if (result.Ok && result.DirPath is not null)
            {
                // Victoria 3 是 metadata 系：封面落在 .metadata	humbnail.png。
                var preview = fs.Combine(fs.Combine(result.DirPath, ".metadata"), "thumbnail.png");
                ChecksContext.Check(
                    "新建 mod：封面与内置封面一致",
                    fs.FileExists(preview) && fs.ReadAllBytes(preview).SequenceEqual(bytes));
            }
            else
            {
                ChecksContext.Check("新建 mod：成功时给出目录", false, (string?)result.ErrorCode ?? "no-error");
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

}
