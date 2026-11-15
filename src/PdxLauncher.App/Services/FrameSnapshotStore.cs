namespace PdxLauncher.App.Services;

using System.Globalization;
using System.Text;
using System.Text.Json;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 上一次成功上屏的整窗背景（只记路径，不记图像）。
/// <para>
/// 用途：冷启动时游戏发现要 200~700 ms，而背景解码只有它知道「当前游戏的主题图」之后才开始。
/// 把上一次的结论落一份到 <c>data\frame-snapshot.json</c>，启动时就能先把这张图在后台解好，
/// 等游戏发现确认「还是这一张」之后直接上屏 —— 解码与发现重叠，背景早一截出现。
/// </para>
/// </summary>
/// <param name="Version">快照格式版本；版本不认识时整份当不存在。</param>
/// <param name="GameId">写快照时的当前游戏 id，只作诊断与校验提示。</param>
/// <param name="BackgroundPath">已上屏背景图的完整路径。</param>
/// <param name="SavedAtUtc">写入时间（UTC）。</param>
public sealed record FrameSnapshot(int Version, string? GameId, string? BackgroundPath, DateTimeOffset SavedAtUtc);

/// <summary>
/// 背景预热快照的读写。文件很小（几百字节），读失败 / 缺失 / 损坏 / 版本不符一律返回 null，
/// 写失败只记日志：它只是省一次解码的优化，任何情况都不允许影响启动。
/// </summary>
public static class FrameSnapshotStore
{
    /// <summary>当前快照格式版本；改了字段就 +1，旧文件会被当作不存在。</summary>
    public const int CurrentVersion = 1;

    /// <summary>文件名（放在数据根目录下）。</summary>
    public const string FileName = "frame-snapshot.json";

    /// <summary>数据根目录 → 快照文件的默认路径。</summary>
    public static string PathIn(string dataRoot) => Path.Combine(dataRoot, FileName);

    /// <summary>读快照；不存在 / 损坏 / 版本不符都返回 null。</summary>
    public static FrameSnapshot? Load(IFileSystem fileSystem, IAppLogger logger, string target)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            if (string.IsNullOrWhiteSpace(target) || !fileSystem.FileExists(target))
            {
                return null;
            }

            var text = fileSystem.ReadAllText(target);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var version = root.TryGetProperty("version", out var versionElement)
                && versionElement.ValueKind == JsonValueKind.Number
                && versionElement.TryGetInt32(out var parsedVersion)
                    ? parsedVersion
                    : 0;
            if (version != CurrentVersion)
            {
                return null;
            }

            DateTimeOffset savedAt = DateTimeOffset.UtcNow;
            if (root.TryGetProperty("savedAtUtc", out var savedElement)
                && savedElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    savedElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedSavedAt))
            {
                savedAt = parsedSavedAt;
            }

            return new FrameSnapshot(
                version,
                ReadString(root, "gameId"),
                ReadString(root, "backgroundPath"),
                savedAt);
        }
        catch (JsonException ex)
        {
            logger.Warn("背景快照解析失败，按不存在处理: " + ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.Warn("读取背景快照失败，按不存在处理: " + ex.Message);
            return null;
        }
    }

    /// <summary>写快照（原子写）；失败只记日志。</summary>

    public static void Save(IFileSystem fileSystem, IAppLogger logger, FrameSnapshot snapshot, string target)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                fileSystem.EnsureDirectory(directory);
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", snapshot.Version);
                writer.WriteString("gameId", snapshot.GameId ?? string.Empty);
                writer.WriteString("backgroundPath", snapshot.BackgroundPath ?? string.Empty);
                writer.WriteString(
                    "savedAtUtc",
                    snapshot.SavedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            fileSystem.WriteAllTextAtomic(target, Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.Warn("写入背景快照失败（不影响启动）: " + ex.Message);
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString())
            : null;
}
