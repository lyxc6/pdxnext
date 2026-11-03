namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 找到某款游戏的 <c>appmanifest_&lt;appid&gt;.acf</c>，并按官方启动器的口径判断
/// 清单里的 <c>DisabledDLC</c> 能不能采信。
/// <para>
/// 官方做法是从游戏安装目录往上两级（<c>&lt;lib&gt;\steamapps\common\&lt;dir&gt;</c>）拼出清单路径，
/// 并且要求 <c>LastOwner</c> 等于当前 SteamID64——否则跨账号残留的 <c>DisabledDLC</c> 会让
/// 界面把别人停用的 DLC 当成「已拥有」。
/// </para>
/// </summary>
public sealed class AppManifestLookup
{
    private const int MaxAncestorLevels = 8;

    private readonly IFileSystem _fileSystem;
    private readonly AppManifestReader _reader;

    /// <summary>构造。</summary>
    public AppManifestLookup(IFileSystem fileSystem, IAppLogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _reader = new AppManifestReader(
            _fileSystem,
            logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    /// <summary>
    /// 找到这款游戏的清单：先用安装目录推回来的库（与官方一致），
    /// 再在已知库里找安装路径匹配的那一份；都没有返回 null。
    /// </summary>
    public AppManifest? Find(GameInstallation game, int appId, IReadOnlyList<string>? libraryRoots)
    {
        ArgumentNullException.ThrowIfNull(game);

        var derived = FindLibraryRoot(game.InstallRoot);
        if (derived is not null)
        {
            var manifest = _reader.ReadForApp(derived, appId);
            if (manifest is not null)
            {
                return manifest;
            }
        }

        if (libraryRoots is null)
        {
            return null;
        }

        foreach (var root in libraryRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (derived is not null && string.Equals(Normalize(root), Normalize(derived), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var manifest = _reader.ReadForApp(root, appId);
            if (manifest is not null && InstallPathMatches(manifest, game.InstallRoot))
            {
                return manifest;
            }
        }

        return null;
    }

    /// <summary>从安装目录往上找「含 <c>steamapps</c> 子目录」的那一层，即 Steam 库根；找不到返回 null。</summary>
    public string? FindLibraryRoot(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return null;
        }

        var current = installRoot;
        for (var level = 0; level < MaxAncestorLevels; level++)
        {
            if (string.IsNullOrEmpty(current))
            {
                return null;
            }

            if (_fileSystem.DirectoryExists(_fileSystem.Combine(current, "steamapps")))
            {
                return current;
            }

            current = _fileSystem.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>
    /// 可信的 <c>DisabledDLC</c>：<paramref name="steamId64"/> 为 0 或清单的 <c>LastOwner</c>
    /// 与它不一致时返回空集（官方 <c>Ict</c> 的跨账号残留保护）。
    /// </summary>
    public static IReadOnlySet<string> TrustedDisabledDlc(AppManifest? manifest, ulong steamId64)
    {
        if (manifest is null || steamId64 == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var owner = manifest.LastOwner;
        if (string.IsNullOrWhiteSpace(owner)
            || !string.Equals(owner.Trim(), steamId64.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return manifest.DisabledDlcSet;
    }

    private static bool InstallPathMatches(AppManifest manifest, string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return false;
        }

        return string.Equals(
            Normalize(manifest.InstallPath),
            Normalize(installRoot),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('/', '\\').TrimEnd('\\');
    }
}
