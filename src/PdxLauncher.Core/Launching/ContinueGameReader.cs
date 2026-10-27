namespace PdxLauncher.Core.Launching;

using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// <see cref="IContinueGameReader"/> 实现：只读 <c>&lt;GameDataDir&gt;\continue_game.json</c>（由游戏写入）。
/// <para>容错要求：文件不存在/为空/JSON 损坏/字段类型不对时返回 null 并记日志，绝不抛异常。
/// Stellaris 的 continue_game.json 没有 <c>rawGameVersion</c> 键，必须允许该字段缺失。</para>
/// </summary>
public sealed class ContinueGameReader : IContinueGameReader
{
    /// <summary>继续游戏信息文件名（游戏写、启动器只读）。</summary>
    public const string FileName = "continue_game.json";

    private const string KeyTitle = "title";
    private const string KeyDescription = "desc";
    private const string KeyDate = "date";
    private const string KeyRawGameVersion = "rawGameVersion";

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>创建继续游戏信息读取器。</summary>
    public ContinueGameReader(IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>continue_game.json 的完整路径。</summary>
    public string GetFilePath(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return _fileSystem.Combine(game.GameDataDir, FileName);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// 整个读取体在 <see cref="Task.Run(Func{ContinueGameInfo?}, CancellationToken)"/> 里执行：
    /// 内部全是同步文件 API（<c>FileExists</c> / <c>ReadAllText</c> / <c>JsonDocument.Parse</c>）。
    /// 直接在调用方线程跑会把磁盘 IO 压在 UI 线程上 —— 游戏装在机械盘或网络盘时，
    /// 每次切换游戏都会卡住整窗（这是 2026-10-27 「主页切换很慢」的直接原因之一）。
    /// </para>
    /// <para>异常语义不变：四类异常仍然在这里兜掉并返回 null，调用方不需要 try/catch。</para>
    /// </remarks>
    public Task<ContinueGameInfo?> ReadAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() => ReadCore(game, cancellationToken), cancellationToken);
    }

    private ContinueGameInfo? ReadCore(GameInstallation game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetFilePath(game);
        try
        {
            if (!_fileSystem.FileExists(path))
            {
                _logger.Info("continue_game.json 不存在，视为无可继续存档：" + path);
                return null;
            }

            var text = _fileSystem.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.Warn("continue_game.json 为空，视为无可继续存档：" + path);
                return null;
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.Warn("continue_game.json 根节点不是对象，视为无可继续存档：" + path);
                return null;
            }

            var title = ReadString(root, KeyTitle);
            var description = ReadString(root, KeyDescription);
            var date = ReadString(root, KeyDate);
            var rawGameVersion = ReadString(root, KeyRawGameVersion);

            if (string.IsNullOrWhiteSpace(title))
            {
                _logger.Warn("continue_game.json 缺少 title，视为无可继续存档：" + path);
                return null;
            }

            return new ContinueGameInfo(title, description, date, rawGameVersion);
        }
        catch (JsonException exception)
        {
            _logger.Warn("continue_game.json 解析失败，视为无可继续存档：" + path + "：" + exception.Message);
            return null;
        }
        catch (IOException exception)
        {
            _logger.Warn("continue_game.json 读取失败，视为无可继续存档：" + path + "：" + exception.Message);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("continue_game.json 无读取权限，视为无可继续存档：" + path + "：" + exception.Message);
            return null;
        }
        catch (ArgumentException exception)
        {
            _logger.Warn("continue_game.json 路径非法，视为无可继续存档：" + path + "：" + exception.Message);
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
