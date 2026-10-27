namespace PdxLauncher.Core.Playsets.PlaysetConfig;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// CS2 的 <c>playset_config_lock.json</c> 写锁。
/// <para>
/// 官方 Electron 启动器的实现（app.asar 逆向）：
/// </para>
/// <list type="bullet">
/// <item>文件内容 <c>{ "configVersion": 1, "lockHolder": "&lt;持有者&gt;", "createdAt": "&lt;iso&gt;", "updatedAt": "&lt;iso&gt;" }</c>，
/// 2 空格缩进。</item>
/// <item>锁超时 <b>300 秒</b>，持有者每 <b>240 秒</b> 续期一次（即刷新 <c>updatedAt</c>）。</item>
/// <item>只有 <c>lockHolder === "Paradox Launcher"</c> 才能释放「官方那把自己写的锁」——
/// 也就是说：别的程序写下的锁，官方<b>不会</b>替它删，但官方会按超时把它当过期。</item>
/// </list>
/// <para>
/// 我们的策略（保守）：
/// </para>
/// <list type="number">
/// <item>现存锁的持有者是 <c>Paradox Launcher</c> 且 <c>updatedAt</c> 在 300 秒内 → <b>拒绝写</b>（官方正在写）。</item>
/// <item>现存锁是别的主张（包括我们自己的 <c>PdxNext</c>、或已过期的官方锁）→ 视为可抢占，直接覆盖。</item>
/// <item>自己写下的锁在 <c>finally</c> 里删掉；删除前再确认持有者仍是自己，避免误删别人刚抢到的锁。</item>
/// </list>
/// </summary>
public static class PlaysetConfigLock
{
    /// <summary>我们的锁持有者标记。</summary>
    public const string OurHolder = "PdxNext";

    /// <summary>官方启动器的锁持有者标记。</summary>
    public const string OfficialHolder = "Paradox Launcher";

    /// <summary>官方锁超时（秒）。</summary>
    public const int TimeoutSeconds = 300;

    private const string KeyConfigVersion = "configVersion";
    private const string KeyLockHolder = "lockHolder";
    private const string KeyCreatedAt = "createdAt";
    private const string KeyUpdatedAt = "updatedAt";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };

    /// <summary>现存锁的信息。</summary>
    /// <param name="Exists">锁文件是否存在。</param>
    /// <param name="Holder">持有者标记（缺失为空串）。</param>
    /// <param name="UpdatedAt">最后续期时间；无法解析为 null。</param>
    /// <param name="IsStale">是否已过期（无法判断时间时保守地视为未过期）。</param>
    public readonly record struct State(bool Exists, string Holder, DateTimeOffset? UpdatedAt, bool IsStale)
    {
        /// <summary>是否被官方启动器持有且仍然有效 —— 此时我们不许写。</summary>
        public bool BlocksWrite =>
            Exists
            && !IsStale
            && string.Equals(Holder, OfficialHolder, StringComparison.Ordinal);
    }

    /// <summary>读现存锁；不存在返回 <c>Exists=false</c>。解析失败按「存在且未过期且非官方」处理（会被抢占）。</summary>
    public static State Inspect(IFileSystem fileSystem, IAppLogger logger, string lockPath, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        if (!fileSystem.FileExists(lockPath))
        {
            return new State(false, string.Empty, null, IsStale: true);
        }

        try
        {
            var node = JsonNode.Parse(fileSystem.ReadAllText(lockPath), documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (node is not JsonObject root)
            {
                logger.Warn("playset_config_lock.json 根节点不是对象，按可抢占处理。");
                return new State(true, string.Empty, null, IsStale: true);
            }

            var holder = PlaysetConfigDocument.GetString(root, KeyLockHolder);
            var updatedAt = DateTimeOffset.TryParse(
                PlaysetConfigDocument.GetString(root, KeyUpdatedAt),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : (DateTimeOffset?)null;

            // 时间不可解析时保守地视为「刚被持有」，不抢官方锁。
            var isStale = updatedAt is { } stamp && (now - stamp) > TimeSpan.FromSeconds(TimeoutSeconds);
            return new State(true, holder, updatedAt, isStale);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.Warn("读取 playset_config_lock.json 失败，按可抢占处理: " + ex.Message);
            return new State(true, string.Empty, null, IsStale: true);
        }
    }

    /// <summary>
    /// 尝试加锁。被官方锁挡住时返回 <see cref="ErrorKind.Conflict"/>（错误码 <c>playset-config-locked</c>）。
    /// </summary>
    public static AppResult<bool> Acquire(
        IFileSystem fileSystem,
        IAppLogger logger,
        string lockPath,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        var state = Inspect(fileSystem, logger, lockPath, now);
        if (state.BlocksWrite)
        {
            logger.Warn(
                "CS2 的 playset 锁被官方启动器持有（updatedAt=" +
                (state.UpdatedAt?.ToString("o", CultureInfo.InvariantCulture) ?? "未知") + "），本次写入放弃。");
            return AppResult<bool>.Fail(ErrorKind.Conflict, "playset-config-locked:official");
        }

        var directory = fileSystem.GetDirectoryName(lockPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.EnsureDirectory(directory);
        }

        var payload = new JsonObject
        {
            [KeyConfigVersion] = PlaysetConfigDocument.DefaultConfigVersion,
            [KeyLockHolder] = OurHolder,
            [KeyCreatedAt] = now.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            [KeyUpdatedAt] = now.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
        };

        fileSystem.WriteAllTextAtomic(lockPath, payload.ToJsonString(WriteOptions));
        return AppResult<bool>.Ok(true);
    }

    /// <summary>
    /// 释放自己的锁。持有者已经不是自己（别人抢到了）时什么都不做，返回 false。
    /// 删不掉只记 Warn，不影响已经写完的文件。
    /// </summary>
    public static bool Release(IFileSystem fileSystem, IAppLogger logger, string lockPath, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            if (!fileSystem.FileExists(lockPath))
            {
                return false;
            }

            var state = Inspect(fileSystem, logger, lockPath, now);
            if (state.Exists && state.Holder.Length > 0
                && !string.Equals(state.Holder, OurHolder, StringComparison.Ordinal))
            {
                logger.Warn("playset_config_lock.json 的持有者已变为 " + state.Holder + "，不删它。");
                return false;
            }

            fileSystem.Delete(lockPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warn("删除 playset_config_lock.json 失败（不影响已写内容）: " + ex.Message);
            return false;
        }
    }
}
