namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 设置读写自检：全字段往返一致、缺文件/坏文件回退默认、延迟写能被 Flush 立刻落盘。
/// 用临时目录，不碰真实 data\settings.json。
/// </summary>
public static class SettingsChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var directory = Path.Combine(Path.GetTempPath(), "pdx-settings-check-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");

        try
        {
            CheckRoundTrip(fs, path);
            CheckMissingFile(fs, path);
            CheckBrokenFile(fs, path);
            CheckDeferredFlush(fs, path);
        }
        finally
        {
            if (fs.FileExists(path))
            {
                fs.Delete(path);
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckRoundTrip(IFileSystem fs, string path)
    {
        var original = new AppSettings
        {
            UiLanguage = "en-US",
            WindowWidth = 1280.5,
            WindowHeight = 800.25,
            WindowX = 320,
            WindowY = -1,
            LastGameId = "ck3",
            LastGameInstallRoot = @"D:\Games\CK3",
            LastSettingsSection = 4,
            AlwaysOnTop = true,
            PdxRememberMe = true,
            ProxyMode = ProxyModes.Manual,
            ProxyUrl = "http://127.0.0.1:7890",
        };

        using (var store = new AppSettingsStore(fs, new SilentLogger(), path))
        {
            var saved = store.Save(original);
            ChecksContext.Check("设置：保存成功", saved.IsSuccess, saved.ErrorMessage);
        }

        using (var store = new AppSettingsStore(fs, new SilentLogger(), path))
        {
            var loaded = store.Load();
            ChecksContext.Equal("设置：界面语言往返", "en-US", loaded.UiLanguage);
            ChecksContext.Equal("设置：窗口宽往返", 1280.5, loaded.WindowWidth);
            ChecksContext.Equal("设置：窗口高往返", 800.25, loaded.WindowHeight);
            ChecksContext.Equal("设置：窗口 X 往返", 320, loaded.WindowX);
            ChecksContext.Equal("设置：窗口 Y 往返", -1, loaded.WindowY);
            ChecksContext.Equal("设置：上次游戏往返", "ck3", loaded.LastGameId);
            ChecksContext.Equal("设置：上次安装目录往返", @"D:\Games\CK3", loaded.LastGameInstallRoot);
            ChecksContext.Equal("设置：设置页分节往返", 4, loaded.LastSettingsSection ?? -1);
            ChecksContext.Check("设置：置顶往返", loaded.AlwaysOnTop);
            ChecksContext.Check("设置：保持登录往返", loaded.PdxRememberMe);
            ChecksContext.Equal("设置：代理模式往返", ProxyModes.Manual, loaded.ProxyMode);
            ChecksContext.Equal("设置：代理地址往返", "http://127.0.0.1:7890", loaded.ProxyUrl);
        }

        // null 字段按 WhenWritingNull 不写进文件。
        var text = fs.ReadAllText(path);
        ChecksContext.Check("设置：null 字段不落盘", !text.Contains("\"lastSettingsSection\": null", StringComparison.Ordinal), text);
    }

    private static void CheckMissingFile(IFileSystem fs, string path)
    {
        if (fs.FileExists(path))
        {
            fs.Delete(path);
        }

        using var store = new AppSettingsStore(fs, new SilentLogger(), path);
        var loaded = store.Load();
        ChecksContext.Equal("设置：缺文件时窗口 X 默认 -1", -1, loaded.WindowX);
        ChecksContext.Equal("设置：缺文件时窗口 Y 默认 -1", -1, loaded.WindowY);
        ChecksContext.Check("设置：缺文件时没有记住的分节", loaded.LastSettingsSection is null);
        ChecksContext.Check("设置：缺文件时不崩溃（默认语言）", loaded.UiLanguage is null or "zh-CN" or "en-US");
    }

    private static void CheckBrokenFile(IFileSystem fs, string path)
    {
        fs.EnsureDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        fs.WriteAllTextAtomic(path, "{ 这不是 JSON");

        using var store = new AppSettingsStore(fs, new SilentLogger(), path);
        var loaded = store.Load();
        ChecksContext.Equal("设置：坏文件回退默认（窗口 X）", -1, loaded.WindowX);
        ChecksContext.Check("设置：坏文件回退默认（游戏）", loaded.LastGameId is null);
    }

    private static void CheckDeferredFlush(IFileSystem fs, string path)
    {
        var settings = new AppSettings { LastGameId = "victoria3" };

        using var store = new AppSettingsStore(fs, new SilentLogger(), path);

        store.SaveDeferred(settings);
        ChecksContext.Check("设置：延迟写在 Flush 前处于待写状态", store.HasPendingWrite);

        var flushed = store.Flush();
        ChecksContext.Check("设置：Flush 成功", flushed.IsSuccess, flushed.ErrorMessage);
        ChecksContext.Check("设置：Flush 后没有待写", !store.HasPendingWrite);

        using var reload = new AppSettingsStore(fs, new SilentLogger(), path);
        ChecksContext.Equal("设置：Flush 后值已落盘", "victoria3", reload.Load().LastGameId);
    }

    private sealed class SilentLogger : IAppLogger
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
