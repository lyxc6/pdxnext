namespace PdxLauncher.Checks;

using System.Text.Json.Nodes;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.PdxSdk;
using PdxLauncher.Core.Playsets.PlaysetConfig;

using PdxConfig = PDX.SDK.Contracts.Configuration.Config;
using PdxEnvironment = PDX.SDK.Contracts.Enums.BackendEnvironment;
using PdxEcosystem = PDX.SDK.Contracts.Enums.Ecosystem;

/// <summary>
/// 官方 <c>PDX.SDK</c> 接入自检：配置映射、会话桥接的读写往返、官方 SDK 的实际加载与
/// <c>Account.Startup()</c>（未登录时应返回 <c>IsLoggedIn=false</c> 而不是抛出）。
/// <para>
/// 不联网：SDK 的 Startup 在没有任何 token 时只读本地缓存；用于钉住「程序集能加载、
/// 配置字段没写错、会话落点与 SDK 的 database.json 完全一致」这三件事。
/// </para>
/// </summary>
public static class PdxSdkChecks
{
    private static readonly PdxSession Session =
        new("TOKEN-1", "REFRESH-1", "u-1", "a@b.c", "Tester", DateTimeOffset.UtcNow);

    /// <summary>跑全部断言。</summary>
    /// <param name="fs">文件系统（会话桥接写临时目录）。</param>
    public static int Run(IFileSystem fs)
    {
        var before = ChecksContext.Failures;

        CheckConfigMapping();
        CheckSessionBridge(fs);
        CheckSdkLoads();

        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// <c>BuildConfig</c> 必须照 Skyve 的 <c>WorkshopService.Initialize()</c>：
    /// <c>DiskIORoot</c>/<c>Mods.RootPath</c>/<c>Ecosystem=Steam</c>/<c>UsePatching=true</c>/User-Agent。
    /// </summary>
    private static void CheckConfigMapping()
    {
        var options = new PdxSdkOptions(
            PlatformGameId: "cities_skylines_2",
            ModsRootPath: @"C:\games\cs2\.cache\Mods",
            DiskIORoot: @"C:\data\pdxsdk\u-1",
            UserId: "u-1",
            Session: Session,
            GameVersion: "1.2.3");

        var config = PdxSdkHost.BuildConfig(options, new NullLogger(), "1.4.3");

        ChecksContext.Equal("配置: namespace 由 host 持有", "cities_skylines_2", options.PlatformGameId);
        ChecksContext.Equal("配置: DiskIORoot 原样传入", @"C:\data\pdxsdk\u-1", config.DiskIORoot);
        ChecksContext.Equal("配置: Mods.RootPath 指向游戏 .cache\\Mods", @"C:\games\cs2\.cache\Mods", config.Mods.RootPath);
        ChecksContext.Equal("配置: Ecosystem=Steam", PdxEcosystem.Steam, config.Ecosystem);
        ChecksContext.Equal("配置: Environment=Live", PdxEnvironment.Live, config.Environment);
        ChecksContext.Equal("配置: UsePatching=true", true, config.Mods.UsePatching);
        ChecksContext.Equal("配置: GameVersion 透传", "1.2.3", config.GameVersion);
        ChecksContext.Check(
            "配置: User-Agent 带版本",
            config.DefaultHeaders is not null
            && config.DefaultHeaders.TryGetValue("User-Agent", out var ua)
            && ua == "PdxNext/1.4.3");
        ChecksContext.Check("配置: 关闭遥测", config.StandardTelemetryEnabled == false);
        ChecksContext.Check("配置: 不跳过合法文档检查", config.SkipLegalChecksAndEnableTelemetry == false);
    }

    /// <summary>
    /// 会话桥接：写进去的字段名必须与 SDK 的 <c>CachedData</c> 完全一致
    /// （<c>SessionToken</c>/<c>RefreshToken</c>/<c>TrustLevel</c>/<c>Namespace</c>/<c>UserId</c>/<c>LoggedIn</c>），
    /// 并且只覆盖会话键、保留 SDK 自己写的其它字段。
    /// </summary>
    private static void CheckSessionBridge(IFileSystem fs)
    {
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-checks", "pdx-sdk-" + Guid.NewGuid().ToString("N")[..8]);
        fs.EnsureDirectory(root);

        try
        {
            PdxSdkSessionBridge.WriteSession(root, Session, "cities_skylines_2");
            var path = PdxSdkSessionBridge.GetDatabasePath(root);
            ChecksContext.Check("会话桥接: 写出 database.json", fs.FileExists(path));

            var json = JsonNode.Parse(fs.ReadAllText(path)) as JsonObject;
            ChecksContext.Equal("会话桥接: SessionToken", "TOKEN-1", GetString(json, "SessionToken"));
            ChecksContext.Equal("会话桥接: RefreshToken", "REFRESH-1", GetString(json, "RefreshToken"));
            ChecksContext.Equal("会话桥接: Namespace", "cities_skylines_2", GetString(json, "Namespace"));
            ChecksContext.Equal("会话桥接: UserId", "u-1", GetString(json, "UserId"));
            ChecksContext.Equal("会话桥接: TrustLevel", "Full", GetString(json, "TrustLevel"));
            ChecksContext.Check(
                "会话桥接: LoggedIn=true",
                json?["LoggedIn"] is JsonValue logged && logged.TryGetValue<bool>(out var flag) && flag);

            var cached = PdxSdkSessionBridge.Read(root);
            ChecksContext.Check("会话桥接: 读回非空", cached is not null);
            ChecksContext.Equal("会话桥接: 读回 token 一致", "TOKEN-1", cached?.SessionToken ?? "<null>");
            ChecksContext.Equal("会话桥接: 读回登录态", true, cached?.LoggedIn ?? false);

            // 再写一次：保留 SDK 自己加的 PseudonymizedUserData。
            json!["PseudonymizedUserData"] = new JsonObject { ["PseudonymizedUserId"] = "pdx_pseudo_x" };
            fs.WriteAllTextAtomic(path, json.ToJsonString());
            PdxSdkSessionBridge.WriteSession(root, Session, "cities_skylines_2");
            var merged = JsonNode.Parse(fs.ReadAllText(path)) as JsonObject;
            ChecksContext.Equal(
                "会话桥接: 合并保留 SDK 的其它字段",
                "pdx_pseudo_x",
                GetString(merged?["PseudonymizedUserData"] as JsonObject, "PseudonymizedUserId"));

            // 登出：删掉会话缓存。
            ChecksContext.Check("会话桥接: Remove 真的删除", PdxSdkSessionBridge.Remove(root) && !fs.FileExists(path));
            ChecksContext.Check("会话桥接: 删除后读到 null", PdxSdkSessionBridge.Read(root) is null);
        }
        finally
        {
            try
            {
                if (fs.DirectoryExists(root))
                {
                    fs.Delete(root);
                }
            }
            catch
            {
                // 清理失败不影响断言。
            }
        }
    }

    /// <summary>
    /// 官方 SDK 的加载冒烟：建 <c>Context</c> + <c>Account.Startup()</c>。
    /// 没有会话时必须返回 <c>IsLoggedIn=false</c>，不能抛异常；同时 <c>GetActivePlayset</c>
    /// 在空播放集时应给出可读错误而不是崩溃。
    /// </summary>
    private static void CheckSdkLoads()
    {
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-checks", "pdx-sdk-load-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var mods = Path.Combine(root, ".cache", "Mods");
        Directory.CreateDirectory(mods);

        PdxSdkHost? host = null;
        try
        {
            var options = new PdxSdkOptions(
                PlatformGameId: "cities_skylines_2",
                ModsRootPath: mods,
                DiskIORoot: Path.Combine(root, "sdk"),
                UserId: "anonymous",
                Session: null);

            host = new PdxSdkHost(options, new NullLogger(), "checks");
            host.EnsureStartedAsync().GetAwaiter().GetResult();
            ChecksContext.Check("SDK: Context.Create 成功", true);
            ChecksContext.Check("SDK: 无会话时 Startup 不抛异常", true);
            ChecksContext.Check("SDK: 无会话时 IsLoggedIn=false", !host.IsLoggedIn);

            var state = host.GetActivePlaySetAsync().GetAwaiter().GetResult();
            ChecksContext.Check(
                "SDK: 空播放集时返回 NotActive 或可读失败",
                state.Status is PdxSdkPlaySetStatus.NotActive or PdxSdkPlaySetStatus.Failed);
        }
        catch (Exception ex)
        {
            ChecksContext.Check("SDK: 加载/初始化不应抛出", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try
            {
                if (host is not null)
                {
                    host.ShutdownAsync().GetAwaiter().GetResult();
                }
            }
            catch
            {
                // 关闭失败不影响断言。
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响断言。
            }
        }
    }

    private static string GetString(JsonObject? node, string key) =>
        node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? string.Empty : string.Empty;

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
