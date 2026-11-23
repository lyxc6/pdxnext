namespace PdxLauncher.Core.PdxSdk;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Accounts;

/// <summary>
/// 把本启动器保存的 <see cref="PdxSession"/> 写进官方 SDK 的会话缓存
/// （<c>&lt;DiskIORoot&gt;\database.json</c>），让 <c>Account.Startup()</c> 直接复用登录态。
/// <para>
/// SDK 的 <c>CachedData</c> 是明文字段（<c>SessionToken</c>/<c>RefreshToken</c>/<c>UserId</c>/
/// <c>Namespace</c>/<c>LoggedIn</c> 等）。本项目自己的会话仍由 <c>PdxSessionStore</c> 用 DPAPI 保护；
/// 这里只是把同一份凭据镜像给 SDK。写入用临时文件 + 原子替换，避免 SDK 读到半截 JSON。
/// </para>
/// <para>
/// <b>已知取舍</b>：SDK 只认明文 <c>database.json</c>，所以该文件在磁盘上是明文。
/// 它位于本启动器的数据目录，不是游戏目录；<see cref="Remove"/> 在登出/切换账号时清掉它。
/// </para>
/// </summary>
public static class PdxSdkSessionBridge
{
    /// <summary>SDK 的会话缓存文件名。</summary>
    public const string DatabaseFileName = "database.json";

    /// <summary>返回某个 <paramref name="diskIoRoot"/> 下 SDK 会话文件的绝对路径。</summary>
    /// <param name="diskIoRoot">SDK 状态目录。</param>
    public static string GetDatabasePath(string diskIoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskIoRoot);
        return Path.Combine(diskIoRoot, DatabaseFileName);
    }

    /// <summary>
    /// 把 <paramref name="session"/> 写进 SDK 的会话缓存。
    /// 已有文件保留 SDK 自己写的其它字段（如 <c>PseudonymizedUserData</c>），只覆盖会话相关键。
    /// </summary>
    /// <param name="diskIoRoot">SDK 状态目录。</param>
    /// <param name="session">本地会话。</param>
    /// <param name="namespace">SDK 的 namespace（平台游戏 id）。</param>
    public static void WriteSession(string diskIoRoot, PdxSession session, string @namespace)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);

        var path = GetDatabasePath(diskIoRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing ? existing : new JsonObject();
            }
            catch (JsonException)
            {
                // 损坏的缓存不该让登录失败：重建一份最小可用文档。
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        root["SessionToken"] = session.SessionToken;
        root["RefreshToken"] = session.RefreshToken ?? string.Empty;
        root["TrustLevel"] = "Full";
        root["Namespace"] = @namespace;
        root["UserId"] = session.UserId;
        root["LoggedIn"] = true;
        if (root["ViewedDocuments"] is not JsonArray)
        {
            root["ViewedDocuments"] = new JsonArray();
        }

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>删除 SDK 会话缓存（登出或切换账号时用）。</summary>
    /// <param name="diskIoRoot">SDK 状态目录。</param>
    public static bool Remove(string diskIoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskIoRoot);
        var path = GetDatabasePath(diskIoRoot);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>
    /// 读出 SDK 缓存里的会话相关字段；没有文件或解析失败返回 null。
    /// 自检与排障用，不参与运行时的登录判断。
    /// </summary>
    /// <param name="diskIoRoot">SDK 状态目录。</param>
    public static PdxsdkCachedSession? Read(string diskIoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskIoRoot);
        var path = GetDatabasePath(diskIoRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null)
            {
                return null;
            }

            return new PdxsdkCachedSession(
                SessionToken: GetString(root, "SessionToken"),
                RefreshToken: GetString(root, "RefreshToken"),
                UserId: GetString(root, "UserId"),
                Namespace: GetString(root, "Namespace"),
                LoggedIn: root["LoggedIn"] is JsonValue logged && logged.TryGetValue<bool>(out var flag) && flag);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static string GetString(JsonObject root, string key) =>
        root[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? string.Empty : string.Empty;
}

/// <summary>SDK 会话缓存里与会话相关的字段（只读快照）。</summary>
/// <param name="SessionToken">会话 token。</param>
/// <param name="RefreshToken">续期 token。</param>
/// <param name="UserId">账号 id。</param>
/// <param name="Namespace">平台游戏 id。</param>
/// <param name="LoggedIn">SDK 记录的登录标志。</param>
public sealed record PdxsdkCachedSession(
    string SessionToken,
    string RefreshToken,
    string UserId,
    string Namespace,
    bool LoggedIn);
