namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// <see cref="ModThumbnailResolver"/> 自检：库重定位、picture 优先、多扩展名、失效路径不抛。
/// 全部在临时目录里构造假 Steam 库，库列表显式注入，绝不依赖本机 Steam 安装。
/// </summary>
public static class ModThumbnailChecks
{
    private const string GameId = "stellaris";
    private const int StellarisAppId = 281990;

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-thumb-" + Guid.NewGuid().ToString("N"));

        try
        {
            fs.EnsureDirectory(tempRoot);

            var library = Path.Combine(tempRoot, "SteamLib");
            var installRoot = Path.Combine(library, "steamapps", "common", "Stellaris");
            var gameDataDir = Path.Combine(tempRoot, "data");
            var game = CreateGame(installRoot, gameDataDir, StellarisAppId);
            var resolver = new ModThumbnailResolver(fs, logger, () => new[] { library });

            CheckDirectDirectory(fs, resolver, game, tempRoot);
            CheckWorkshopRelocation(fs, resolver, game, library);
            CheckWorkshopIdRelocation(fs, resolver, game, library);
            CheckRegistryFallback(fs, resolver, game, library);
            CheckPicturePriority(fs, resolver, game, tempRoot);
            CheckPictureSubdirectory(fs, resolver, game, tempRoot);
            CheckFileNameVariants(fs, resolver, game, tempRoot);
            CheckMetadataDirectory(fs, resolver, game, tempRoot);
            CheckArchiveDirectory(fs, resolver, game, tempRoot);
            CheckCachedPath(fs, resolver, game, tempRoot);
            CheckMissingAndInvalidInput(fs, resolver, game, tempRoot);
            CheckEmptyLibraryIsolation(fs, logger, tempRoot);
        }
        finally
        {
            TryDelete(fs, tempRoot);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckDirectDirectory(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] dirPath 直接命中");
        var directory = Path.Combine(tempRoot, "mods", "direct");
        var expected = Path.Combine(directory, "thumbnail.png");
        WriteImage(fs, expected);

        var actual = resolver.Resolve(game, dirPath: directory);
        ChecksContext.Equal("dirPath 命中 thumbnail.png", expected, actual ?? "<null>");
    }

    private static void CheckWorkshopRelocation(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string library)
    {
        Console.WriteLine("[ModThumbnailChecks] 库搬迁后按 steamapps 后缀重定位");
        // 数据库里记录的旧盘路径（实际不存在），真实内容在新库的同一相对位置。
        var stalePath = Path.Combine("E:", "SteamLibrary", "steamapps", "workshop", "content", "281990", "111");
        var expected = Path.Combine(library, "steamapps", "workshop", "content", "281990", "111", "thumbnail.png");
        WriteImage(fs, expected);

        var actual = resolver.Resolve(game, dirPath: stalePath);
        ChecksContext.Equal("旧盘 dirPath 重定位到新库", expected, actual ?? "<null>");
    }

    private static void CheckWorkshopIdRelocation(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string library)
    {
        Console.WriteLine("[ModThumbnailChecks] dirPath 缺失时按 appid + steamId 重定位");
        var expected = Path.Combine(library, "steamapps", "workshop", "content", "281990", "222", "thumbnail.png");
        WriteImage(fs, expected);

        var actual = resolver.Resolve(game, steamId: "222");
        ChecksContext.Equal("只有 steamId 也能定位", expected, actual ?? "<null>");
    }

    private static void CheckRegistryFallback(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string library)
    {
        Console.WriteLine("[ModThumbnailChecks] steamId 缺失时按 gameRegistryId 反解创意工坊 id");
        var expected = Path.Combine(library, "steamapps", "workshop", "content", "281990", "333", "thumbnail.png");
        WriteImage(fs, expected);

        var actual = resolver.Resolve(game, gameRegistryId: "mod/ugc_333.mod");
        ChecksContext.Equal("mod/ugc_*.mod 反解命中", expected, actual ?? "<null>");
    }

    private static void CheckPicturePriority(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] picture 优先于 thumbnail.png");
        var directory = Path.Combine(tempRoot, "mods", "picture");
        WriteImage(fs, Path.Combine(directory, "thumbnail.png"));
        var declared = Path.Combine(directory, "Cover Art.jpg");
        WriteImage(fs, declared);

        var actual = resolver.Resolve(game, dirPath: directory, picture: "Cover Art.jpg");
        ChecksContext.Equal("picture 声明的图片优先", declared, actual ?? "<null>");

        var fallback = resolver.Resolve(game, dirPath: directory, picture: "missing.png");
        ChecksContext.Equal(
            "picture 指向不存在的文件时回退 thumbnail.png",
            Path.Combine(directory, "thumbnail.png"),
            fallback ?? "<null>");
    }

    private static void CheckPictureSubdirectory(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] picture 带子目录");
        var directory = Path.Combine(tempRoot, "mods", "subdir");
        var declared = Path.Combine(directory, "gfx", "thumb", "x.png");
        WriteImage(fs, declared);

        var actual = resolver.Resolve(game, dirPath: directory, picture: "gfx/thumb/x.png");
        ChecksContext.Equal("picture 子目录命中", declared, actual ?? "<null>");
    }

    private static void CheckFileNameVariants(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] thumbnail 文件名变体");
        var preferred = Path.Combine(tempRoot, "mods", "variants");
        WriteImage(fs, Path.Combine(preferred, "thumbnail_new.png"));
        WriteImage(fs, Path.Combine(preferred, "thumbnail.png"));
        var actual = resolver.Resolve(game, dirPath: preferred);
        ChecksContext.Equal(
            "同时存在 thumbnail_new.png 时仍取 thumbnail.png",
            Path.Combine(preferred, "thumbnail.png"),
            actual ?? "<null>");

        var jpegOnly = Path.Combine(tempRoot, "mods", "jpeg");
        var jpeg = Path.Combine(jpegOnly, "thumbnail.jpg");
        WriteImage(fs, jpeg);
        var actualJpeg = resolver.Resolve(game, dirPath: jpegOnly);
        ChecksContext.Equal("只有 thumbnail.jpg 也能命中", jpeg, actualJpeg ?? "<null>");

        var noisy = Path.Combine(tempRoot, "mods", "noisy");
        var png = Path.Combine(noisy, "thumbnail_old.png");
        WriteImage(fs, png);
        // Photoshop / Paint.NET 工程文件不能渲染，必须跳过。
        fs.WriteAllBytesAtomic(Path.Combine(noisy, "thumbnail.pdn"), new byte[] { 1, 2, 3 });
        var actualNoisy = resolver.Resolve(game, dirPath: noisy);
        ChecksContext.Equal("thumbnail*.pdn 不会被当成缩略图", png, actualNoisy ?? "<null>");
    }

    private static void CheckArchiveDirectory(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] archivePath 同目录");
        var directory = Path.Combine(tempRoot, "archives");
        var expected = Path.Combine(directory, "thumbnail.png");
        WriteImage(fs, expected);

        var actual = resolver.Resolve(game, archivePath: Path.Combine(directory, "mod.zip"));
        ChecksContext.Equal("归档 mod 取压缩包同目录缩略图", expected, actual ?? "<null>");
    }

    private static void CheckCachedPath(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] sqlite thumbnailPath 兜底");
        var directory = Path.Combine(tempRoot, "cache");
        var cached = Path.Combine(directory, "cached.png");
        WriteImage(fs, cached);
        var empty = Path.Combine(tempRoot, "mods", "empty-cache");
        fs.EnsureDirectory(empty);

        var actual = resolver.Resolve(game, dirPath: empty, cachedThumbnailPath: cached);
        ChecksContext.Equal("本地无图时用 sqlite 缓存路径", cached, actual ?? "<null>");

        var actualMissing = resolver.Resolve(game, dirPath: empty, cachedThumbnailPath: Path.Combine(directory, "gone.png"));
        ChecksContext.Check("sqlite 缓存路径不存在时返回 null", actualMissing is null, actualMissing ?? "<null>");
    }

    private static void CheckMissingAndInvalidInput(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] 缺图与非法输入");
        var empty = Path.Combine(tempRoot, "mods", "no-image");
        fs.EnsureDirectory(empty);

        ChecksContext.Check("目录里没有图片时返回 null", resolver.Resolve(game, dirPath: empty) is null);
        ChecksContext.Check("dirPath 为空白时返回 null", resolver.Resolve(game, dirPath: "   ") is null);
        ChecksContext.Check("全部参数为 null 时返回 null", resolver.Resolve(game) is null);

        // 非法路径字符不能让异常冒到 UI（ReadAsync 只吞 SqliteException / IOException）。
        var threw = false;
        string? invalidResult = null;
        try
        {
            invalidResult = resolver.Resolve(game, dirPath: "C:\\bad\0name");
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        ChecksContext.Check("非法路径不抛 ArgumentException", !threw);
        ChecksContext.Check("非法路径返回 null", invalidResult is null, invalidResult ?? "<null>");
    }

    private static void CheckEmptyLibraryIsolation(IFileSystem fs, IAppLogger logger, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] 空库列表即完全隔离");
        // 安装目录刻意不放在 steamapps 下：确保既没有库重定位也没有 InstallRoot 推导。
        var installRoot = Path.Combine(tempRoot, "standalone", "Stellaris");
        var game = CreateGame(installRoot, Path.Combine(tempRoot, "data2"), StellarisAppId);
        var resolver = new ModThumbnailResolver(fs, logger, () => Array.Empty<string>());
        var stalePath = Path.Combine("E:", "SteamLibrary", "steamapps", "workshop", "content", "281990", "444");

        ChecksContext.Check(
            "注入空库列表后不做任何 Steam 库探测",
            resolver.Resolve(game, dirPath: stalePath, steamId: "444") is null);
    }

    /// <summary>
    /// EU5 / PDX Mods 真机布局：封面在 <c>&lt;mod&gt;\.metadata\thumbnail.png</c>，mod 根目录没有。
    /// 真机 22/22 个 EU5 工坊 mod 都是这样，只有 6 个在根目录另有副本——所以这里既验新回退，
    /// 也验「根目录优先」不被破坏。
    /// </summary>
    private static void CheckMetadataDirectory(IFileSystem fs, ModThumbnailResolver resolver, GameInstallation game, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailChecks] .metadata 封面（EU5 布局）");

        var onlyMetadata = Path.Combine(tempRoot, "mods", "eu5-metadata");
        var expected = Path.Combine(onlyMetadata, ".metadata", "thumbnail.png");
        WriteImage(fs, expected);
        var actual = resolver.Resolve(game, dirPath: onlyMetadata);
        ChecksContext.Equal(".metadata 里的封面能命中", expected, actual ?? "<null>");

        var both = Path.Combine(tempRoot, "mods", "eu5-both");
        var rootImage = Path.Combine(both, "thumbnail.png");
        WriteImage(fs, rootImage);
        WriteImage(fs, Path.Combine(both, ".metadata", "thumbnail.png"));
        var actualBoth = resolver.Resolve(game, dirPath: both);
        ChecksContext.Equal("根目录 thumbnail.png 仍优先于 .metadata 副本", rootImage, actualBoth ?? "<null>");

        var jpeg = Path.Combine(tempRoot, "mods", "eu5-metadata-jpeg");
        var jpegImage = Path.Combine(jpeg, ".metadata", "thumbnail.jpg");
        WriteImage(fs, jpegImage);
        var actualJpeg = resolver.Resolve(game, dirPath: jpeg);
        ChecksContext.Equal(".metadata 里只有 thumbnail.jpg 也能命中", jpegImage, actualJpeg ?? "<null>");

        // metadata.json 的 picture 相对 .metadata 解释（EU5 13/22 个 mod 带这个键）。
        var declared = Path.Combine(tempRoot, "mods", "eu5-picture");
        var declaredImage = Path.Combine(declared, ".metadata", "cover art.png");
        WriteImage(fs, declaredImage);
        var actualDeclared = resolver.Resolve(game, dirPath: declared, picture: "cover art.png");
        ChecksContext.Equal("picture 相对 .metadata 命中", declaredImage, actualDeclared ?? "<null>");

        var flat = Path.Combine(tempRoot, "mods", "eu5-picture-flat");
        var flatImage = Path.Combine(flat, ".metadata", "cover.png");
        WriteImage(fs, flatImage);
        var actualFlat = resolver.Resolve(game, dirPath: flat, picture: "gfx/cover.png");
        ChecksContext.Equal("picture 带子目录时退化到 .metadata 平铺", flatImage, actualFlat ?? "<null>");

        var noImage = Path.Combine(tempRoot, "mods", "eu5-no-image");
        fs.EnsureDirectory(Path.Combine(noImage, ".metadata"));
        fs.WriteAllTextAtomic(Path.Combine(noImage, ".metadata", "metadata.json"), "{}");
        ChecksContext.Check("只有 metadata.json 时返回 null", resolver.Resolve(game, dirPath: noImage) is null);

        var pdnOnly = Path.Combine(tempRoot, "mods", "eu5-metadata-pdn");
        fs.WriteAllBytesAtomic(Path.Combine(pdnOnly, ".metadata", "thumbnail.pdn"), new byte[] { 1, 2, 3 });
        ChecksContext.Check(".metadata 里只有 thumbnail.pdn 时返回 null", resolver.Resolve(game, dirPath: pdnOnly) is null);

        // 库搬家 + .metadata 组合：数据库里的旧盘 dirPath 已失效，真图在新库的工坊目录里。
        var stalePath = Path.Combine(
            "E:", "SteamLibrary", "steamapps", "workshop", "content",
            StellarisAppId.ToString(System.Globalization.CultureInfo.InvariantCulture), "555");
        var relocated = Path.Combine(
            tempRoot, "SteamLib", "steamapps", "workshop", "content",
            StellarisAppId.ToString(System.Globalization.CultureInfo.InvariantCulture), "555", ".metadata", "thumbnail.png");
        WriteImage(fs, relocated);
        var actualRelocated = resolver.Resolve(game, dirPath: stalePath);
        ChecksContext.Equal("陈旧 dirPath 重定位后仍取 .metadata 封面", relocated, actualRelocated ?? "<null>");
    }

    private static void WriteImage(IFileSystem fs, string path)
        => fs.WriteAllBytesAtomic(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

    private static GameInstallation CreateGame(string installRoot, string gameDataDir, int? steamAppId)
        => new(
            GameId: GameId,
            DisplayName: "Stellaris",
            Version: "test",
            InstallRoot: installRoot,
            LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
            Format: LoadFileFormat.DlcLoadV0,
            GameDataDir: gameDataDir,
            DlcPath: Path.Combine(installRoot, "dlc"),
            ModDir: Path.Combine(gameDataDir, "mod"),
            ExePath: Path.Combine(installRoot, "stellaris.exe"),
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
            SteamAppId: steamAppId,
            Support: GameSupport.Full);

    private static void TryDelete(IFileSystem fs, string path)
    {
        try
        {
            if (fs.DirectoryExists(path))
            {
                fs.Delete(path);
            }
        }
        catch (IOException exception)
        {
            Console.WriteLine("  warn 清理临时目录失败：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.WriteLine("  warn 清理临时目录无权限：" + exception.Message);
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
