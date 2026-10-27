namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Parsing;

/// <summary>
/// 定位 Steam 库目录：先读注册表拿 Steam 安装路径，再解析
/// <c>&lt;steam&gt;\steamapps\libraryfolders.vdf</c> 得到所有库。
/// 注册表读取没有 IFileSystem 抽象可用（此处按任务约定直接用 Microsoft.Win32.Registry），
/// 其余全部走 <see cref="IFileSystem"/>。
/// </summary>
public sealed class SteamLibraryLocator
{
    private const string SteamKeyPath = @"Software\Valve\Steam";
    private const string SteamKeyPathWow64 = @"SOFTWARE\WOW6432Node\Valve\Steam";
    private const string SteamPathValueName = "SteamPath";
    private const string SteamInstallPathValueName = "InstallPath";

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    public SteamLibraryLocator(IFileSystem fileSystem, IAppLogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>当前 Steam 安装根目录；找不到返回 null。</summary>
    public string? FindSteamRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.Warn("非 Windows 平台，跳过 Steam 注册表探测");
            return null;
        }

        var fromHkcu = ReadRegistryValue(Microsoft.Win32.RegistryHive.CurrentUser, SteamKeyPath, SteamPathValueName);
        if (!string.IsNullOrWhiteSpace(fromHkcu) && _fileSystem.DirectoryExists(Normalize(fromHkcu)))
        {
            return Normalize(fromHkcu);
        }

        var fromHklm = ReadRegistryValue(Microsoft.Win32.RegistryHive.LocalMachine, SteamKeyPathWow64, SteamInstallPathValueName);
        if (!string.IsNullOrWhiteSpace(fromHklm) && _fileSystem.DirectoryExists(Normalize(fromHklm)))
        {
            return Normalize(fromHklm);
        }

        // 兜底：注册表里写的是相对/失效路径时，回退到 HKLM 原值（不校验存在性）
        if (!string.IsNullOrWhiteSpace(fromHklm))
        {
            return Normalize(fromHklm);
        }

        if (!string.IsNullOrWhiteSpace(fromHkcu))
        {
            return Normalize(fromHkcu);
        }

        _logger.Warn("未能从注册表读到 Steam 安装路径");
        return null;
    }

    /// <summary>
    /// 返回所有 Steam 库根目录（含 Steam 自身的安装目录），去重、按发现顺序排列。
    /// </summary>
    public IReadOnlyList<string> FindLibraries()
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 注意顺序：vdf 里的库路径是用户磁盘上的真实大小写（例如 D:\app\Steam），
        // 而注册表的 SteamPath 常常是安装时写死的小写形态（d:/app/steam）。
        // 必须先收 vdf 的路径、最后再补 steamRoot，否则小写形态会先占坑，
        // 把 vdf 里正确大小写的同一条库判为重复而丢弃，进而污染 InstallRoot/ExePath。
        var steamRoot = FindSteamRoot();
        if (steamRoot is not null)
        {
            var vdfPath = _fileSystem.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            foreach (var library in ReadLibraryFolders(vdfPath))
            {
                if (_fileSystem.DirectoryExists(library) && seen.Add(library))
                {
                    results.Add(library);
                }
            }
        }

        if (steamRoot is not null && _fileSystem.DirectoryExists(steamRoot) && seen.Add(steamRoot))
        {
            results.Add(steamRoot);
        }

        if (results.Count == 0)
        {
            _logger.Warn("未发现任何 Steam 库");
        }

        return results;
    }

    /// <summary>
    /// 解析 libraryfolders.vdf，取出全部 <c>path</c> 值。
    /// 兼容新旧两种 schema：新的是 <c>"0" { "path" "..." }</c>，旧的是 <c>"1" "D:\\Steam"</c>。
    /// </summary>
    public IReadOnlyList<string> ReadLibraryFolders(string libraryFoldersVdfPath)
    {
        ArgumentNullException.ThrowIfNull(libraryFoldersVdfPath);
        var results = new List<string>();
        if (!_fileSystem.FileExists(libraryFoldersVdfPath))
        {
            _logger.Warn("libraryfolders.vdf 不存在: " + libraryFoldersVdfPath);
            return results;
        }

        ValveKeyValuesNode root;
        try
        {
            root = ValveKeyValuesParser.ParseFile(_fileSystem, libraryFoldersVdfPath);
        }
        catch (IOException ex)
        {
            _logger.Warn("解析 libraryfolders.vdf 失败: " + libraryFoldersVdfPath + " (" + ex.Message + ")");
            return results;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("无权读取 libraryfolders.vdf: " + libraryFoldersVdfPath + " (" + ex.Message + ")");
            return results;
        }

        var container = root.FindChild("libraryfolders") ?? root;
        foreach (var child in container.Children)
        {
            if (child.IsBlock)
            {
                var path = child.GetValue("path");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    results.Add(Normalize(path));
                }

                continue;
            }

            // 旧 schema：键是序号，值是路径本身（跳过 "contentstatsid" 之类的元数据键）
            if (child.Value is null || child.Value.Length == 0)
            {
                continue;
            }

            var looksLikeIndex = int.TryParse(child.Key, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _);
            if (looksLikeIndex && (child.Value.Contains(":\\", StringComparison.Ordinal) || child.Value.Contains(":/", StringComparison.Ordinal)))
            {
                results.Add(Normalize(child.Value));
            }
        }

        return results;
    }

    private string? ReadRegistryValue(Microsoft.Win32.RegistryHive hive, string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath, writable: false);
            var value = key?.GetValue(valueName) as string;
            return value;
        }
        catch (System.Security.SecurityException ex)
        {
            _logger.Warn("读取注册表被拒绝: " + hive + "\\" + keyPath + " (" + ex.Message + ")");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("无权读取注册表: " + hive + "\\" + keyPath + " (" + ex.Message + ")");
            return null;
        }
        catch (IOException ex)
        {
            _logger.Warn("读取注册表失败: " + hive + "\\" + keyPath + " (" + ex.Message + ")");
            return null;
        }
    }

    private string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');
}
