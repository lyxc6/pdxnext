namespace PdxLauncher.Core.Themes;

using System.Globalization;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;

/// <summary>
/// 读取安装目录里的 <c>theme-settings.json</c>（本地主题，三层解析里的"安装目录"层）。
/// 候选位置：
/// <c>&lt;root&gt;\launcher\assets\</c>（CK3 / V3）、<c>&lt;root&gt;\assets\</c>（Stellaris）、
/// <c>&lt;root&gt;\Launcher-assets\</c> 与 <c>&lt;root&gt;\Launcher\Launcher-assets\</c>（Prison Architect）、
/// 以及安装根目录本身。若 launcher-settings.json 给了 themeFile（已绝对化），优先用它。
/// 图片/字体路径相对 theme-settings.json 所在目录绝对化；颜色原样保留字符串（由 App 层转换）。
/// 文件缺失或损坏时返回 <see cref="Placeholder"/>，绝不抛异常。
/// </summary>
public sealed class ThemeReader
{
    private const string ThemeFileName = "theme-settings.json";

    /// <summary>主题文件缺失时使用的按钮文字色（官方 button.color 是文字色，不是底色）。</summary>
    public const string DefaultButtonTextColor = "#ffffff";

    /// <summary>主题文件缺失时使用的按钮悬停文字色。</summary>
    public const string DefaultButtonHoverTextColor = "#ffffff";

    /// <summary>主题文件缺失时的默认字号。</summary>
    public const double DefaultFontSize = 24.0;

    /// <summary>主题文件缺失时的默认字重。</summary>
    public const int DefaultFontWeight = 400;

    private static readonly string[] CandidateRelativePaths =
    {
        "launcher/assets/" + ThemeFileName,
        "assets/" + ThemeFileName,
        "Launcher-assets/" + ThemeFileName,
        "Launcher/Launcher-assets/" + ThemeFileName,
        ThemeFileName,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    public ThemeReader(IFileSystem fileSystem, IAppLogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>内置占位主题（无任何本地素材）。</summary>
    public static ThemeSpec Placeholder() => new(
        Background: null,
        Logo: null,
        AppIcon: null,
        AppTaskbarIcon: null,
        ButtonBackground: null,
        ButtonFont: null,
        ButtonTextColor: DefaultButtonTextColor,
        ButtonHoverTextColor: DefaultButtonHoverTextColor,
        FontSize: DefaultFontSize,
        FontWeight: DefaultFontWeight,
        TextShadow: null,
        Source: ThemeAssetSource.BuiltInPlaceholder);

    /// <summary>
    /// 定位某个安装根目录的 theme-settings.json；找不到返回 null。
    /// <paramref name="themeFile"/> 为 launcher-settings.json 里的 themeFile（可为 null）。
    /// </summary>
    public string? FindThemeFile(string installRoot, string? themeFile = null)
    {
        ArgumentNullException.ThrowIfNull(installRoot);

        if (!string.IsNullOrWhiteSpace(themeFile))
        {
            var resolved = CaseInsensitivePath.Resolve(_fileSystem, themeFile);
            if (resolved is not null && _fileSystem.FileExists(resolved))
            {
                return resolved;
            }

            if (_fileSystem.FileExists(themeFile))
            {
                return themeFile;
            }
        }

        if (!_fileSystem.DirectoryExists(installRoot))
        {
            return null;
        }

        foreach (var relative in CandidateRelativePaths)
        {
            var candidate = _fileSystem.Combine(installRoot, relative.Replace('/', '\\'));
            var resolved = CaseInsensitivePath.Resolve(_fileSystem, candidate);
            if (resolved is not null && _fileSystem.FileExists(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    /// <summary>
    /// 读取安装目录主题。找不到/解析失败返回 <see cref="Placeholder"/>（Source = BuiltInPlaceholder）。
    /// </summary>
    public ThemeSpec Read(string installRoot, string? themeFile = null)
    {
        var path = FindThemeFile(installRoot, themeFile);
        if (path is null)
        {
            _logger.Info("未找到本地主题文件: " + installRoot);
            return Placeholder();
        }

        return ReadFile(path);
    }

    /// <summary>读取指定的 theme-settings.json；失败返回 <see cref="Placeholder"/>。</summary>
    public ThemeSpec ReadFile(string themeSettingsPath)
    {
        ArgumentNullException.ThrowIfNull(themeSettingsPath);

        string text;
        try
        {
            text = _fileSystem.ReadAllText(themeSettingsPath);
        }
        catch (IOException ex)
        {
            _logger.Warn("读取主题文件失败: " + themeSettingsPath + " (" + ex.Message + ")");
            return Placeholder();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("无权读取主题文件: " + themeSettingsPath + " (" + ex.Message + ")");
            return Placeholder();
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.Warn("主题文件根节点不是对象，用占位主题: " + themeSettingsPath);
                return Placeholder();
            }

            var baseDirectory = _fileSystem.GetDirectoryName(themeSettingsPath) ?? string.Empty;
            return Parse(root, baseDirectory, themeSettingsPath);
        }
        catch (JsonException ex)
        {
            _logger.Warn("主题文件解析失败，用占位主题: " + themeSettingsPath + " (" + ex.Message + ")");
            return Placeholder();
        }
    }

    private ThemeSpec Parse(JsonElement root, string baseDirectory, string themeSettingsPath)
    {
        var buttonTextColor = ReadNestedString(root, "button", "color") ?? DefaultButtonTextColor;
        var buttonHoverTextColor = ReadNestedString(root, "buttonHover", "color") ?? buttonTextColor;

        return new ThemeSpec(
            Background: AbsoluteOrNull(baseDirectory, ReadNestedString(root, "background", "backgroundImage")),
            Logo: AbsoluteOrNull(baseDirectory, ReadNestedString(root, "logo", "imagePath")),
            AppIcon: AbsoluteOrNull(baseDirectory, ReadNestedString(root, "appIcon", "appIconPath")),
            AppTaskbarIcon: AbsoluteOrNull(baseDirectory, ReadNestedString(root, "appTaskbarIcon", "appIconPath")),
            ButtonBackground: AbsoluteOrNull(baseDirectory, ReadNestedString(root, "button", "backgroundImage")),
            ButtonFont: AbsoluteOrNull(baseDirectory, ReadNestedString(root, "button", "fontPath")),
            ButtonTextColor: buttonTextColor,
            ButtonHoverTextColor: buttonHoverTextColor,
            FontSize: ReadNestedDouble(root, "button", "fontSize") ?? DefaultFontSize,
            FontWeight: ReadNestedInt(root, "button", "fontWeight") ?? DefaultFontWeight,
            TextShadow: ReadNestedString(root, "button", "textShadow"),
            Source: ThemeAssetSource.InstallDir);
    }

    private string? AbsoluteOrNull(string baseDirectory, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var normalized = relative.Replace('/', '\\');
        if (Path.IsPathRooted(normalized))
        {
            return _fileSystem.GetFullPath(normalized);
        }

        if (string.IsNullOrEmpty(baseDirectory))
        {
            return _fileSystem.GetFullPath(normalized);
        }

        return _fileSystem.GetFullPath(_fileSystem.Combine(baseDirectory, normalized));
    }

    private static JsonElement? ReadSection(JsonElement root, string section)
    {
        if (!root.TryGetProperty(section, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return node;
    }

    private static string? ReadNestedString(JsonElement root, string section, string property)
    {
        var node = ReadSection(root, section);
        if (node is null || !node.Value.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static double? ReadNestedDouble(JsonElement root, string section, string property)
    {
        var node = ReadSection(root, section);
        if (node is null || !node.Value.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ReadNestedInt(JsonElement root, string section, string property)
    {
        var node = ReadSection(root, section);
        if (node is null || !node.Value.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
