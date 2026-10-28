namespace PdxLauncher.Core.Patch;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>一个待处理的 steam_api 文件：所在目录 + 文件名 + 架构。</summary>
internal sealed record PatchTarget(string ApiDirectory, string ApiFileName, bool IsX64)
{
    /// <summary>完整路径。</summary>
    public string ApiPath => Path.Combine(ApiDirectory, ApiFileName);
}

/// <summary>
/// 各游戏的 steam_api 文件位置规则。
/// <para>
/// Paradox 游戏的 loader 位置并不统一：CS1/EU4/HOI4/Stellaris 放在游戏根目录，
/// CK3/EU5/Imperator/Victoria 3 放在 <c>binaries\</c>，CS2 在
/// <c>Cities2_Data\Plugins\x86_64\</c>。这里按「exe 所在目录 → 安装根 → binaries」的顺序
/// 找第一个同时含 api 文件的目录，命中即止；x64 优先，两种架构都在时两个都处理。
/// </para>
/// </summary>
internal static class GamePatchProfiles
{
    private const string CitiesSkylines2GameId = "cities_skylines_2";

    /// <summary>APK 文件所在的候选目录（去重，按优先级）。</summary>
    internal static IReadOnlyList<string> CandidateDirectories(IFileSystem fs, GameInstallation game)
    {
        var list = new List<string>(4);

        // CS2 的 api 藏在 Unity 的 Plugins 目录里，exe 旁边和根目录都没有。
        if (string.Equals(game.GameId, CitiesSkylines2GameId, StringComparison.OrdinalIgnoreCase))
        {
            Add(list, fs.Combine(game.InstallRoot, "Cities2_Data", "Plugins", "x86_64"));
        }

        var exeDirectory = fs.GetDirectoryName(game.ExePath);
        if (!string.IsNullOrWhiteSpace(exeDirectory))
        {
            Add(list, exeDirectory);
        }

        Add(list, game.InstallRoot);
        Add(list, fs.Combine(game.InstallRoot, "binaries"));
        return list;
    }

    /// <summary>找出要处理的 api 文件；没有任何 api 文件时返回空表。</summary>
    internal static IReadOnlyList<PatchTarget> Resolve(IFileSystem fs, GameInstallation game)
    {
        foreach (var directory in CandidateDirectories(fs, game))
        {
            var targets = new List<PatchTarget>(2);
            if (fs.FileExists(fs.Combine(directory, PatchAssets.Loader64FileName)))
            {
                targets.Add(new PatchTarget(directory, PatchAssets.Loader64FileName, IsX64: true));
            }

            if (fs.FileExists(fs.Combine(directory, PatchAssets.Loader86FileName)))
            {
                targets.Add(new PatchTarget(directory, PatchAssets.Loader86FileName, IsX64: false));
            }

            if (targets.Count > 0)
            {
                return targets;
            }
        }

        return Array.Empty<PatchTarget>();
    }

    private static void Add(List<string> list, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        foreach (var existing in list)
        {
            if (string.Equals(existing, directory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        list.Add(directory);
    }
}
