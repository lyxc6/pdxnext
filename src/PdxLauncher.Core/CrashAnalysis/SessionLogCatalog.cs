namespace PdxLauncher.Core.CrashAnalysis;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>一条本次会话需要读取的日志来源。</summary>
/// <param name="Name">展示与去重用的稳定名称。</param>
/// <param name="Path">绝对路径。</param>
public sealed record SessionLogSource(string Name, string Path);

/// <summary>本次游戏会话的日志来源清单。</summary>
public static class SessionLogCatalog
{
    private const string CitiesSkylines2GameId = "cities_skylines_2";

    private static readonly string[] CommonNames =
    [
        "error.log", "game.log", "debug.log", "system.log", "graphics.log", "warning.log", "database_conflicts.log",
    ];

    private static readonly (string Name, string RelativePath)[] CitiesSkylines2Names =
    [
        ("Player.log", "Player.log"),
        ("SceneFlow.log", @"Logs\SceneFlow.log"),
        ("UI.log", @"Logs\UI.log"),
    ];

    /// <summary>
    /// 返回该游戏要扫描的日志。CS2 用根目录下的 Unity Player.log 与 Logs 子目录里的日志文件，
    /// 其余 Paradox 游戏沿用崩溃分析器共用的 logs 清单。
    /// </summary>
    public static IReadOnlyList<SessionLogSource> Resolve(IFileSystem fileSystem, GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(game.GameDataDir))
        {
            return Array.Empty<SessionLogSource>();
        }

        var sources = new List<SessionLogSource>();
        if (string.Equals(game.GameId, CitiesSkylines2GameId, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (name, relativePath) in CitiesSkylines2Names)
            {
                sources.Add(new SessionLogSource(name, fileSystem.Combine(game.GameDataDir, relativePath)));
            }

            return sources;
        }

        var logsDirectory = fileSystem.Combine(game.GameDataDir, "logs");
        foreach (string name in CommonNames)
        {
            sources.Add(new SessionLogSource(name, fileSystem.Combine(logsDirectory, name)));
        }

        return sources;
    }
}
