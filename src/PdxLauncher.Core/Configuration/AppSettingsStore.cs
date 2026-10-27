namespace PdxLauncher.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// AppSettings 的读写。文件位于程序目录下的 data\settings.json；
/// 旧位置 %LOCALAPPDATA%\PdxNext 只由 <see cref="DataRootMigrator"/> 读取一次用于迁移。
/// 读取容错：文件缺失/损坏一律返回默认值并记日志，绝不抛异常。
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fs;
    private readonly IAppLogger _logger;
    private readonly string _path;
    private readonly object _gate = new();

    public AppSettingsStore(IFileSystem fs, IAppLogger logger, string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _fs = fs;
        _logger = logger;
        _path = settingsPath;
    }

    public string SettingsPath => _path;

    public static string DefaultPath => Path.Combine(PdxDataRoot.Directory, "settings.json");

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!_fs.FileExists(_path))
                {
                    return new AppSettings();
                }

                var json = _fs.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
            catch (JsonException ex)
            {
                _logger.Warn("settings.json 解析失败，使用默认设置: " + ex.Message);
                return new AppSettings();
            }
            catch (IOException ex)
            {
                _logger.Warn("settings.json 读取失败，使用默认设置: " + ex.Message);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, Options);
                _fs.EnsureDirectory(Path.GetDirectoryName(_path) ?? ".");
                _fs.WriteAllTextAtomic(_path, json);
            }
            catch (IOException ex)
            {
                _logger.Error("settings.json 写入失败", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Error("settings.json 写入失败（无权限）", ex);
            }
        }
    }
}
