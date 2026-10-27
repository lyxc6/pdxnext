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
    public Task<ContinueGameInfo?> ReadAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetFilePath(game);
        try
        {
            if (!_fileSystem.FileExists(path))
            {
                _logger.Info("continue_game.json 不存在，视为无可继续存档：" + path);
                return Task.FromResult<ContinueGameInfo?>(null);
            }

            var text = _fileSystem.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.Warn("continue_game.json 为空，视为无可继续存档：" + path);
                return Task.FromResult<ContinueGameInfo?>(null);
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.Warn("continue_game.json 根节点不是对象，视为无可继续存档：" + path);
                return Task.FromResult<ContinueGameInfo?>(null);
            }

            var title = ReadString(root, KeyTitle);
            var description = ReadString(root, KeyDescription);
            var date = ReadString(root, KeyDate);
            var rawGameVersion = ReadString(root, KeyRawGameVersion);

            if (string.IsNullOrWhiteSpace(title))
            {
                _logger.Warn("continue_game.json 缺少 title，视为无可继续存档：" + path);
                return Task.FromResult<ContinueGameInfo?>(null);
            }

            return Task.FromResult<ContinueGameInfo?>(new ContinueGameInfo(title, description, date, rawGameVersion));
        }
        catch (JsonException exception)
        {
            _logger.Warn("continue_game.json 解析失败，视为无可继续存档：" + path + "：" + exception.Message);
            return Task.FromResult<ContinueGameInfo?>(null);
        }
        catch (IOException exception)
        {
            _logger.Warn("continue_game.json 读取失败，视为无可继续存档：" + path + "：" + exception.Message);
            return Task.FromResult<ContinueGameInfo?>(null);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("continue_game.json 无读取权限，视为无可继续存档：" + path + "：" + exception.Message);
            return Task.FromResult<ContinueGameInfo?>(null);
        }
        catch (ArgumentException exception)
        {
            _logger.Warn("continue_game.json 路径非法，视为无可继续存档：" + path + "：" + exception.Message);
            return Task.FromResult<ContinueGameInfo?>(null);
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
