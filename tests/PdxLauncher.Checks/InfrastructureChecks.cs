namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;

/// <summary>Lead 拥有的基础设施层自检（IFileSystem 原子写 / 日志 / 设置持久化）。</summary>
public static class InfrastructureChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            fs.EnsureDirectory(tempRoot);

            // --- 原子写：UTF-8 无 BOM，内容逐字节一致
            var target = Path.Combine(tempRoot, "sample.json");
            var text = "{\"k\":\"中文不转义\"}";
            fs.WriteAllTextAtomic(target, text);
            ChecksContext.Check("原子写后文件存在", fs.FileExists(target));
            var bytes = fs.ReadAllBytes(target);
            ChecksContext.Check(
                "写入为 UTF-8 无 BOM",
                bytes.Length > 0 && !(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
                "首字节 0x" + (bytes.Length > 0 ? bytes[0].ToString("X2") : "??"));
            ChecksContext.Equal("读回文本一致", text, fs.ReadAllText(target));
            ChecksContext.Check(
                "没有残留 .pdxnext.tmp",
                !fs.FileExists(target + ".pdxnext.tmp"));

            // --- 覆盖写不破坏旧内容语义
            fs.WriteAllTextAtomic(target, "second");
            ChecksContext.Equal("覆盖写生效", "second", fs.ReadAllText(target));

            // --- AppSettingsStore 往返
            var store = new AppSettingsStore(fs, new NullLogger(), Path.Combine(tempRoot, "settings.json"));
            var settings = new AppSettings
            {
                OnlineThemeRefreshEnabled = false,
                ProxyUrl = "http://127.0.0.1:7890",
                WriteDlcSignature = true,
                WindowWidth = 1280,
                WindowHeight = 800,
                PlaySetPageDisplayMode = PlaySetPageDisplayModes.Placeholder,
                AlwaysOnTop = true,
                BackgroundDisplayMode = BackgroundDisplayModes.FullBlur,
                ModThumbnailSize = ModThumbnailSizes.Large,
            };
            store.Save(settings);

            var loaded = store.Load();
            ChecksContext.Equal("设置往返: 联网开关", false, loaded.OnlineThemeRefreshEnabled);
            ChecksContext.Equal("设置往返: 代理", "http://127.0.0.1:7890", loaded.ProxyUrl);
            ChecksContext.Equal("设置往返: 签名开关", true, loaded.WriteDlcSignature);
            ChecksContext.Equal("设置往返: 窗宽", 1280d, loaded.WindowWidth);
            ChecksContext.Equal("设置往返: 置顶", true, loaded.AlwaysOnTop);
            ChecksContext.Check("置顶默认关闭", new AppSettings().AlwaysOnTop == false);
            ChecksContext.Equal(
                "设置往返: 播放集页面显示方式",
                PlaySetPageDisplayModes.Placeholder,
                loaded.PlaySetPageDisplayMode ?? string.Empty);
            ChecksContext.Check(
                "设置不持久化主题检查时间（改由 assets.json 的 lastCheckedUtc 记录）",
                !fs.ReadAllText(Path.Combine(tempRoot, "settings.json")).Contains("lastThemeCheck", StringComparison.OrdinalIgnoreCase));

            // --- 播放集页面显示方式归一化（未知值必须回退原版启动器，不能让整份设置失效）
            ChecksContext.Equal(
                "显示方式: null 回退原版启动器",
                PlaySetPageDisplayModes.OfficialLauncher,
                PlaySetPageDisplayModes.Normalize(null));
            ChecksContext.Equal(
                "显示方式: 空白回退原版启动器",
                PlaySetPageDisplayModes.OfficialLauncher,
                PlaySetPageDisplayModes.Normalize("  "));
            ChecksContext.Equal(
                "显示方式: placeholder 归一化（大小写 / 空白无关）",
                PlaySetPageDisplayModes.Placeholder,
                PlaySetPageDisplayModes.Normalize(" PLACEHOLDER "));
            ChecksContext.Equal(
                "显示方式: 未知值回退原版启动器",
                PlaySetPageDisplayModes.OfficialLauncher,
                PlaySetPageDisplayModes.Normalize("unknown"));
            ChecksContext.Check(
                "显示方式: IsPlaceholder 只认 placeholder",
                PlaySetPageDisplayModes.IsPlaceholder(PlaySetPageDisplayModes.Placeholder)
                    && !PlaySetPageDisplayModes.IsPlaceholder(PlaySetPageDisplayModes.OfficialLauncher));

            // --- 背景显示方式五档 + Mod 图片大小三档
            ChecksContext.Equal(
                "设置往返: 背景显示方式",
                BackgroundDisplayModes.FullBlur,
                loaded.BackgroundDisplayMode ?? string.Empty);
            ChecksContext.Equal(
                "设置往返: Mod 图片大小",
                ModThumbnailSizes.Large,
                loaded.ModThumbnailSize ?? string.Empty);
            ChecksContext.Equal(
                "背景显示: null 回退覆盖",
                BackgroundDisplayModes.Cover,
                BackgroundDisplayModes.Normalize(null));
            ChecksContext.Equal(
                "背景显示: 空白回退覆盖",
                BackgroundDisplayModes.Cover,
                BackgroundDisplayModes.Normalize("  "));
            ChecksContext.Equal(
                "背景显示: 未知值回退覆盖",
                BackgroundDisplayModes.Cover,
                BackgroundDisplayModes.Normalize("huge"));
            ChecksContext.Equal(
                "背景显示: 大小写无关",
                BackgroundDisplayModes.FullBlur,
                BackgroundDisplayModes.Normalize(" FULLBLUR "));
            ChecksContext.Check(
                "背景显示: 只有 fullBlur 需要模糊铺底",
                BackgroundDisplayModes.UsesBlurredBackdrop(BackgroundDisplayModes.FullBlur)
                    && !BackgroundDisplayModes.UsesBlurredBackdrop(BackgroundDisplayModes.Cover)
                    && !BackgroundDisplayModes.UsesBlurredBackdrop(null));
            ChecksContext.Equal("Mod 图片大小: null 取中档", 48, ModThumbnailSizes.Pixels(null));
            ChecksContext.Equal(
                "Mod 图片大小: small=32",
                ModThumbnailSizes.SmallPixels,
                ModThumbnailSizes.Pixels(ModThumbnailSizes.Small));
            ChecksContext.Equal(
                "Mod 图片大小: large=72",
                ModThumbnailSizes.LargePixels,
                ModThumbnailSizes.Pixels(ModThumbnailSizes.Large));
            ChecksContext.Equal("Mod 图片大小: 未知值取中档", ModThumbnailSizes.MediumPixels, ModThumbnailSizes.Pixels("huge"));

            // --- 损坏的设置文件必须容错返回默认值
            var brokenPath = Path.Combine(tempRoot, "broken.json");
            fs.WriteAllTextAtomic(brokenPath, "{ this is not json ");
            var brokenStore = new AppSettingsStore(fs, new NullLogger(), brokenPath);
            var fallback = brokenStore.Load();
            ChecksContext.Check("损坏设置文件不抛异常且回默认值", fallback.OnlineThemeRefreshEnabled);

            // --- 文件日志真实写入
            var logPath = Path.Combine(tempRoot, "logs", "test.log");
            using (var logger = new FileLogger(logPath))
            {
                logger.Info("hello 中文");
                logger.Warn("warn");
                logger.Error("boom", new InvalidOperationException("inner"));
            }

            ChecksContext.Check("日志文件已创建", fs.FileExists(logPath));
            var logText = fs.FileExists(logPath) ? fs.ReadAllText(logPath) : string.Empty;
            ChecksContext.Check("日志含 INFO 行", logText.Contains("[INFO] hello 中文", StringComparison.Ordinal), logText.Length + " B");
            ChecksContext.Check("日志含异常堆栈", logText.Contains("InvalidOperationException", StringComparison.Ordinal));

            // --- 延迟写：登记后不立刻落盘，Flush 才写；连续登记只保留最后一次
            var deferredPath = Path.Combine(tempRoot, "deferred.json");
            using (var deferredStore = new AppSettingsStore(fs, new NullLogger(), deferredPath))
            {
                var deferred = new AppSettings { ProxyUrl = "http://127.0.0.1:1111" };
                deferredStore.SaveDeferred(deferred);
                ChecksContext.Check("延迟写登记后有 pending", deferredStore.HasPendingWrite);
                ChecksContext.Check("延迟写未到期时不落盘", !fs.FileExists(deferredPath));

                deferred.ProxyUrl = "http://127.0.0.1:2222";
                deferredStore.SaveDeferred(deferred);
                ChecksContext.Check("连续登记仍只有一份 pending", deferredStore.HasPendingWrite);

                var flushed = deferredStore.Flush();
                ChecksContext.Check("Flush 返回成功", flushed.IsSuccess, flushed.ErrorMessage);
                ChecksContext.Check("Flush 后没有 pending", !deferredStore.HasPendingWrite);
                ChecksContext.Check("Flush 后文件已落盘", fs.FileExists(deferredPath));
                ChecksContext.Equal(
                    "Flush 落的是最后一次登记的值",
                    "http://127.0.0.1:2222",
                    fs.FileExists(deferredPath) ? deferredStore.Load().ProxyUrl : string.Empty);

                // --- 立即写会取消尚未到期的延迟写，且返回成功
                deferredStore.SaveDeferred(deferred);
                ChecksContext.Check("立即写前仍有 pending", deferredStore.HasPendingWrite);
                var immediate = deferredStore.Save(new AppSettings { ProxyUrl = "http://127.0.0.1:3333" });
                ChecksContext.Check("立即写返回成功", immediate.IsSuccess, immediate.ErrorMessage);
                ChecksContext.Check("立即写取消掉延迟写", !deferredStore.HasPendingWrite);
                ChecksContext.Equal("立即写的内容已落盘", "http://127.0.0.1:3333", deferredStore.Load().ProxyUrl);
            }

            // --- 写失败必须返回失败而不是静默当成功（目录被同名文件占住）
            var blockedRoot = Path.Combine(tempRoot, "blocked");
            fs.WriteAllTextAtomic(blockedRoot, "not a directory");
            var blockedStore = new AppSettingsStore(fs, new NullLogger(), Path.Combine(blockedRoot, "settings.json"));
            var blockedResult = blockedStore.Save(new AppSettings());
            ChecksContext.Check(
                "写失败返回 ErrorKind.Io 而不是抛异常",
                !blockedResult.IsSuccess && blockedResult.Kind == ErrorKind.Io,
                blockedResult.ErrorMessage ?? "(no message)");
            ChecksContext.Check(
                "写失败也取消掉 pending",
                !blockedStore.HasPendingWrite);
            blockedStore.Dispose();

        }
        finally
        {
            try
            {
                if (fs.DirectoryExists(tempRoot))
                {
                    fs.Delete(tempRoot);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
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
