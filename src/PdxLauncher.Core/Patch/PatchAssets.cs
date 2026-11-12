namespace PdxLauncher.Core.Patch;

using System.Reflection;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// <see cref="IPatchAssets"/> 的默认实现：先看外部覆盖目录，再退回程序集内嵌资源。
/// <para>
/// 内嵌资源的逻辑名固定为 <c>PdxLauncher.Patch.*</c>（见 PdxLauncher.Core.csproj 与
/// tools/manual-build.py 的 <c>-resource:</c> 参数），改名会让补丁按钮整体失效。
/// </para>
/// </summary>
public sealed class PatchAssets : IPatchAssets
{
    /// <summary>x64 loader 文件名（Steam 的 64 位 api）。</summary>
    public const string Loader64FileName = "steam_api64.dll";

    /// <summary>x86 loader 文件名（Steam 的 32 位 api）。</summary>
    public const string Loader86FileName = "steam_api.dll";

    /// <summary>补丁配置文件名。</summary>
    public const string IniFileName = "cream_api.ini";

    /// <summary>内嵌资源逻辑名（必须与 .csproj / manual-build.py 里的 LogicalName 一致）。</summary>
    public const string EmbeddedLoader64 = "PdxLauncher.Patch.steam_api64.dll";

    /// <summary>内嵌资源逻辑名（x86）。</summary>
    public const string EmbeddedLoader86 = "PdxLauncher.Patch.steam_api.dll";

    /// <summary>内嵌资源逻辑名（ini 模板）。</summary>
    public const string EmbeddedIni = "PdxLauncher.Patch.cream_api.ini";

    /// <summary>按游戏分节的 DLC 源清单文件名（运行时渲染成 ini 的单一 <c>[dlc]</c> 段）。</summary>
    public const string DlcCatalogFileName = "dlc-catalog.json";

    /// <summary>内嵌资源逻辑名（DLC 源清单）。</summary>
    public const string EmbeddedDlcCatalog = "PdxLauncher.Patch.dlc-catalog.json";

    private readonly IFileSystem _fs;
    private readonly IAppLogger _logger;
    private readonly Assembly _assembly;
    private bool _usedExternal;

    public PatchAssets(IFileSystem fileSystem, IAppLogger logger, string assetsDirectory)
    {
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        AssetsDirectory = assetsDirectory ?? throw new ArgumentNullException(nameof(assetsDirectory));
        _assembly = typeof(PatchAssets).Assembly;
    }

    /// <inheritdoc />
    public string AssetsDirectory { get; }

    /// <inheritdoc />
    public bool UsesExternalAssets => _usedExternal;

    /// <inheritdoc />
    public bool TryGetLoader(bool x64, out byte[] data)
    {
        var fileName = x64 ? Loader64FileName : Loader86FileName;
        var external = _fs.Combine(AssetsDirectory, fileName);
        if (TryReadExternal(external, out data))
        {
            return true;
        }

        return TryReadEmbedded(x64 ? EmbeddedLoader64 : EmbeddedLoader86, out data);
    }

    /// <inheritdoc />
    public bool TryGetIniTemplate(out string text)
    {
        var external = _fs.Combine(AssetsDirectory, IniFileName);
        if (_fs.FileExists(external))
        {
            try
            {
                text = _fs.ReadAllText(external);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _usedExternal = true;
                    _logger.Info("补丁素材使用外部覆盖目录：" + external);
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn("读取外部补丁素材失败，改用内置：" + external + " / " + ex.Message);
            }
        }

        var stream = _assembly.GetManifestResourceStream(EmbeddedIni);
        if (stream is null)
        {
            text = string.Empty;
            return false;
        }

        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        return !string.IsNullOrWhiteSpace(text);
    }

    /// <inheritdoc />
    public bool TryGetDlcCatalog(out PatchDlcCatalog catalog)
    {
        var external = _fs.Combine(AssetsDirectory, DlcCatalogFileName);
        if (_fs.FileExists(external))
        {
            try
            {
                var text = _fs.ReadAllText(external);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    catalog = PatchDlcCatalog.Parse(text);
                    _usedExternal = true;
                    _logger.Info("补丁素材使用外部覆盖目录：" + external);
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _logger.Warn("读取外部 DLC 清单失败，改用内置：" + external + " / " + ex.Message);
            }
        }

        catalog = PatchDlcCatalog.Empty;
        var stream = _assembly.GetManifestResourceStream(EmbeddedDlcCatalog);
        if (stream is null)
        {
            _logger.Error("内置补丁素材缺失：" + EmbeddedDlcCatalog);
            return false;
        }

        string json;
        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            json = reader.ReadToEnd();
        }

        try
        {
            catalog = PatchDlcCatalog.Parse(json);
            return true;
        }
        catch (InvalidDataException ex)
        {
            _logger.Error("内置 DLC 清单无法解析：" + EmbeddedDlcCatalog + " / " + ex.Message);
            return false;
        }
    }

    private bool TryReadExternal(string path, out byte[] data)
    {
        data = Array.Empty<byte>();
        if (!_fs.FileExists(path))
        {
            return false;
        }

        try
        {
            data = _fs.ReadAllBytes(path);
            if (data.Length == 0)
            {
                return false;
            }

            _usedExternal = true;
            _logger.Info("补丁素材使用外部覆盖目录：" + path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("读取外部补丁素材失败，改用内置：" + path + " / " + ex.Message);
            return false;
        }
    }

    private bool TryReadEmbedded(string logicalName, out byte[] data)
    {
        data = Array.Empty<byte>();
        var stream = _assembly.GetManifestResourceStream(logicalName);
        if (stream is null)
        {
            _logger.Error("内置补丁素材缺失：" + logicalName);
            return false;
        }

        using (stream)
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            data = buffer.ToArray();
        }

        return data.Length > 0;
    }
}
