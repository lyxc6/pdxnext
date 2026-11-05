namespace PdxLauncher.Core.Saves;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Games;

/// <summary>
/// 已知游戏的存档落点表。
/// <para>
/// 只登记本任务实测过的落点；目录不存在时 <see cref="Resolve"/> 返回空集合而不是抛异常，
/// 扫描器据此把「游戏没装/没存档」与「读取失败」区分开。
/// </para>
/// <para>
/// 路径依赖四个可注入的根：Documents、LocalAppData、LocalAppDataLow 与文件系统。
/// 生产环境不传参；自检传临时目录，保证不碰真机存档。
/// </para>
/// </summary>
public sealed class SaveLocationCatalog
{
    private const string ParadoxFolder = "Paradox Interactive";
    private const string SaveGamesFolder = "save games";

    private readonly IFileSystem _fileSystem;
    private readonly string _documentsDirectory;
    private readonly string _localAppDataDirectory;
    private readonly string _localAppDataLowDirectory;
    private readonly Dictionary<string, SaveLocationDefinition> _definitions;

    /// <summary>创建存档落点表。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="documentsDirectory">覆盖「我的文档」根；null 时用系统 Known Folder。</param>
    /// <param name="localAppDataDirectory">覆盖 <c>%LOCALAPPDATA%</c>；null 时用系统 Known Folder。</param>
    /// <param name="localAppDataLowDirectory">覆盖 <c>%USERPROFILE%\AppData\LocalLow</c>；null 时由 LocalAppData 推导。</param>
    public SaveLocationCatalog(
        IFileSystem fileSystem,
        string? documentsDirectory = null,
        string? localAppDataDirectory = null,
        string? localAppDataLowDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
        _documentsDirectory = FirstNonEmpty(
            documentsDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        _localAppDataDirectory = FirstNonEmpty(
            localAppDataDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        _localAppDataLowDirectory = FirstNonEmpty(
            localAppDataLowDirectory,
            ResolveLocalAppDataLow(_localAppDataDirectory));
        _definitions = BuildDefinitions();
    }

    /// <summary>登记了落点的游戏 id（已归一，按 Ordinal 排序）。</summary>
    public IReadOnlyList<string> SupportedGameIds =>
        _definitions.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 解析某个游戏的存档落点；未登记或目录不存在时返回空集合。
    /// </summary>
    /// <param name="gameId">游戏 id（大小写不敏感，别名会归一）。</param>
    public IReadOnlyList<SaveLocation> Resolve(string gameId)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        if (canonical.Length == 0 || !_definitions.TryGetValue(canonical, out var definition))
        {
            return [];
        }

        if (!_fileSystem.DirectoryExists(definition.Directory))
        {
            return [];
        }

        return
        [
            new SaveLocation(
                definition.GameId,
                definition.Directory,
                definition.MaxDepth,
                definition.Extensions,
                definition.Container),
        ];
    }

    /// <summary>
    /// 返回某个游戏登记过的存档落点及其是否存在；未登记时返回空集合。
    /// <para>与 <see cref="Resolve"/> 的区别：目录不存在时仍返回一条 <c>Exists=false</c> 的记录，
    /// 供备份层区分「该游戏没有存档目录」与「目录还没出现」。</para>
    /// </summary>
    /// <param name="gameId">游戏 id（大小写不敏感，别名会归一）。</param>
    public IReadOnlyList<SaveLocationInfo> GetLocations(string gameId)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        if (canonical.Length == 0 || !_definitions.TryGetValue(canonical, out var definition))
        {
            return [];
        }

        return
        [
            new SaveLocationInfo(
                definition.GameId,
                definition.Directory,
                _fileSystem.DirectoryExists(definition.Directory),
                definition.Source),
        ];
    }

    private Dictionary<string, SaveLocationDefinition> BuildDefinitions()
    {
        var definitions = new Dictionary<string, SaveLocationDefinition>(StringComparer.OrdinalIgnoreCase);
        var paradox = _fileSystem.Combine(_documentsDirectory, ParadoxFolder);

        Add(
            definitions,
            "ck3",
            _fileSystem.Combine(paradox, "Crusader Kings III", SaveGamesFolder),
            0,
            SaveContainer.TextMeta,
            SaveLocationSource.Documents,
            [".ck3"]);
        Add(
            definitions,
            "victoria3",
            _fileSystem.Combine(paradox, "Victoria 3", SaveGamesFolder),
            0,
            SaveContainer.BinaryToken,
            SaveLocationSource.Documents,
            [".v3"]);
        Add(
            definitions,
            "eu5",
            _fileSystem.Combine(paradox, "Europa Universalis V", SaveGamesFolder),
            0,
            SaveContainer.BinaryToken,
            SaveLocationSource.Documents,
            [".eu5"]);
        Add(
            definitions,
            "stellaris",
            _fileSystem.Combine(paradox, "Stellaris", SaveGamesFolder),
            2,
            SaveContainer.ZipMeta,
            SaveLocationSource.Documents,
            [".sav"]);
        Add(
            definitions,
            "cities_skylines_2",
            _fileSystem.Combine(
                _localAppDataLowDirectory,
                "Colossal Order",
                "Cities Skylines II",
                "Saves"),
            1,
            SaveContainer.Colossal,
            SaveLocationSource.LocalAppDataLow,
            [".cok"]);
        Add(
            definitions,
            "cities_skylines",
            _fileSystem.Combine(
                _localAppDataDirectory,
                "Colossal Order",
                "Cities_Skylines",
                "Saves"),
            0,
            SaveContainer.Colossal,
            SaveLocationSource.LocalAppData,
            [".crp"]);

        return definitions;
    }

    private static void Add(
        IDictionary<string, SaveLocationDefinition> definitions,
        string gameId,
        string directory,
        int maxDepth,
        SaveContainer container,
        SaveLocationSource source,
        string[] extensions)
    {
        definitions[gameId] = new SaveLocationDefinition(gameId, directory, maxDepth, extensions, container, source);
    }

    private static string ResolveLocalAppDataLow(string localAppDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(localAppDataDirectory))
        {
            return string.Empty;
        }

        var parent = Path.GetDirectoryName(localAppDataDirectory);
        return string.IsNullOrEmpty(parent) ? string.Empty : Path.Combine(parent, "LocalLow");
    }

    private static string FirstNonEmpty(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private sealed record SaveLocationDefinition(
        string GameId,
        string Directory,
        int MaxDepth,
        IReadOnlyList<string> Extensions,
        SaveContainer Container,
        SaveLocationSource Source);
}
