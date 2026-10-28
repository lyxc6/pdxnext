namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Playsets;

/// <summary>
/// Mod 库行 → 创意工坊详情的编号解析自检：Steam 优先、CS2 的 pdx: 前缀、本地 mod 不可打开、
/// 来源缺失不猜平台；并核对状态键确实存在于两份 modlib.json 里
/// （Core 引用不到 App 生成的 Keys 常量，键名一旦打错界面只会显示 ⟨key⟩，只能这样兜住）。
/// 不联网、不启动任何进程。
/// </summary>
public static class ModWorkshopRefChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckSteamWins();
        CheckPdxModsSource();
        CheckParadoxSource();
        CheckLocalAndUnknown();
        CheckStatusKeysExist();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>有 steamId 或 ugc_ 编号时一律按 Steam 处理（与「在 Steam 中打开」同一套判定）。</summary>
    private static void CheckSteamWins()
    {
        var bySteamId = ModWorkshopRef.Resolve("2216623537", "mod/ugen_1.mod", ModWorkshopRef.SourceSteam);
        ChecksContext.Equal("Steam：取 steamId", WorkshopSource.Steam, bySteamId.Source);
        ChecksContext.Equal("Steam：编号", "2216623537", bySteamId.Id);
        ChecksContext.Check("Steam：可打开", bySteamId.CanOpen);
        ChecksContext.Equal("Steam：可用时没有原因键", null, bySteamId.StatusTextKey);

        var byRegistry = ModWorkshopRef.Resolve(null, "mod/ugc_778899_extra.mod", null);
        ChecksContext.Equal(
            "Steam：注册 ID 里的 ugc_ 也认（来源缺失时仍按 Steam）",
            "778899",
            byRegistry.Id);
        ChecksContext.Equal("Steam：ugc_ 解析出的平台", WorkshopSource.Steam, byRegistry.Source);

        var spaced = ModWorkshopRef.Resolve("  123  ", "pdx:456", ModWorkshopRef.SourcePdxMods);
        ChecksContext.Equal("Steam：steamId 优先于 pdx: 注册 ID", "123", spaced.Id);
        ChecksContext.Equal("Steam：steamId 优先时平台是 Steam", WorkshopSource.Steam, spaced.Source);

        var fullWidth = ModWorkshopRef.Resolve("２２１６６２３５３７", "pdx:456", ModWorkshopRef.SourcePdxMods);
        ChecksContext.Equal("全角数字的 steamId 不算编号，落到 pdx", "456", fullWidth.Id);
        ChecksContext.Equal("全角数字时平台按 pdx", WorkshopSource.Pdx, fullWidth.Source);
    }

    /// <summary>CS2：注册 ID 形如 pdx:&lt;sourceId&gt;，编号必须是纯数字。</summary>
    private static void CheckPdxModsSource()
    {
        var ok = ModWorkshopRef.Resolve(null, "pdx:134236", ModWorkshopRef.SourcePdxMods);
        ChecksContext.Equal("pdx_mods：剥掉前缀", "134236", ok.Id);
        ChecksContext.Equal("pdx_mods：平台", WorkshopSource.Pdx, ok.Source);
        ChecksContext.Check("pdx_mods：可打开", ok.CanOpen);
        ChecksContext.Equal("pdx_mods：没有原因键", null, ok.StatusTextKey);

        var bare = ModWorkshopRef.Resolve(null, "134236", ModWorkshopRef.SourcePdxMods);
        ChecksContext.Equal("pdx_mods：没有前缀时整串当编号", "134236", bare.Id);

        var named = ModWorkshopRef.Resolve(null, "pdx:my_cool_mod", ModWorkshopRef.SourcePdxMods);
        ChecksContext.Check("pdx_mods：非数字编号不可打开", !named.CanOpen);
        ChecksContext.Equal("pdx_mods：非数字编号为空串", string.Empty, named.Id);
        ChecksContext.Equal(
            "pdx_mods：非数字编号的原因键",
            ModWorkshopRef.StatusKeyPdxUnresolved,
            named.StatusTextKey);

        // long.TryParse 会接受这些写法，拼进 URL 就是打不开的地址，这里确认被拒。
        foreach (var candidate in new[] { "pdx:+12", "pdx:1_2", "pdx:12L", "pdx:1.2" })
        {
            var bad = ModWorkshopRef.Resolve(null, candidate, ModWorkshopRef.SourcePdxMods);
            ChecksContext.Check("pdx_mods：拒绝写法 " + candidate, !bad.CanOpen);
        }

        var empty = ModWorkshopRef.Resolve(null, "pdx:", ModWorkshopRef.SourcePdxMods);
        ChecksContext.Check("pdx_mods：只有前缀没有编号", !empty.CanOpen);
        ChecksContext.Equal(
            "pdx_mods：只有前缀时的原因键",
            ModWorkshopRef.StatusKeyPdxUnresolved,
            empty.StatusTextKey);
    }

    /// <summary>老游戏的 paradox 渠道：注册 ID 有可能直接就是数字 mod id。</summary>
    private static void CheckParadoxSource()
    {
        var paradox = ModWorkshopRef.Resolve(null, "28655", ModWorkshopRef.SourceParadox);
        ChecksContext.Equal("paradox：数字注册 ID 当编号", "28655", paradox.Id);
        ChecksContext.Equal("paradox：平台", WorkshopSource.Pdx, paradox.Source);

        var prefixed = ModWorkshopRef.Resolve(null, "pdx:28655", ModWorkshopRef.SourceParadox);
        ChecksContext.Equal("paradox：带前缀也认", "28655", prefixed.Id);

        var text = ModWorkshopRef.Resolve(null, "mod_descriptor", ModWorkshopRef.SourceParadox);
        ChecksContext.Check("paradox：非数字编号不可打开", !text.CanOpen);
        ChecksContext.Equal("paradox：非数字的原因键", ModWorkshopRef.StatusKeyPdxUnresolved, text.StatusTextKey);
    }

    /// <summary>本地 mod 与认不出的来源：都不可打开，但原因各说各的。</summary>
    private static void CheckLocalAndUnknown()
    {
        var local = ModWorkshopRef.Resolve(null, "mod/abc.mod", ModWorkshopRef.SourceLocal);
        ChecksContext.Check("local：不可打开", !local.CanOpen);
        ChecksContext.Equal("local：编号为空串", string.Empty, local.Id);
        ChecksContext.Equal("local：原因键", ModWorkshopRef.StatusKeyNoWorkshop, local.StatusTextKey);
        ChecksContext.Equal("local：平台为 None", WorkshopSource.None, local.Source);

        var steamNoId = ModWorkshopRef.Resolve("abc", "mod/abc.mod", ModWorkshopRef.SourceSteam);
        ChecksContext.Check("steam：解析不出编号时不可打开", !steamNoId.CanOpen);
        ChecksContext.Equal("steam：原因键", ModWorkshopRef.StatusKeySteamUnresolved, steamNoId.StatusTextKey);
        ChecksContext.Equal("steam：平台仍记 Steam", WorkshopSource.Steam, steamNoId.Source);

        var weirdSource = ModWorkshopRef.Resolve(null, "134236", " workshop");
        ChecksContext.Check("未知来源：不可打开", !weirdSource.CanOpen);
        ChecksContext.Equal("未知来源：原因键", ModWorkshopRef.StatusKeyUnknownSource, weirdSource.StatusTextKey);

        var nullSource = ModWorkshopRef.Resolve(null, "some_local_name", null);
        ChecksContext.Check("来源缺失 + 无编号：不可打开", !nullSource.CanOpen);
        ChecksContext.Equal(
            "来源缺失 + 无编号：原因键",
            ModWorkshopRef.StatusKeyUnknownSource,
            nullSource.StatusTextKey);
    }

    /// <summary>状态键必须能在两份 modlib.json 里找到（键名写错时界面只会显示 ⟨key⟩）。</summary>
    private static void CheckStatusKeysExist()
    {
        string[] keys =
        [
            ModWorkshopRef.StatusKeyNoWorkshop,
            ModWorkshopRef.StatusKeySteamUnresolved,
            ModWorkshopRef.StatusKeyPdxUnresolved,
            ModWorkshopRef.StatusKeyUnknownSource,
            "modlib.openDetail",
        ];

        string[] languages = ["zh-CN", "en-US"];
        foreach (var language in languages)
        {
            var path = RepoPath(Path.Combine("tools", "i18n", language, "modlib.json"));
            if (!File.Exists(path))
            {
                ChecksContext.Check("文案表存在：" + language, false, path);
                continue;
            }

            var lines = File.ReadAllLines(path);
            foreach (var key in keys)
            {
                var needle = "\"" + key + "\":";
                var found = lines.Any(line => line.TrimStart().StartsWith(needle, StringComparison.Ordinal));
                ChecksContext.Check(language + " 有键 " + key, found);
            }
        }
    }

    /// <summary>从输出目录往上找仓库根，拼出仓库内文件路径（同 ChecksContext.FixturePath 的思路）。</summary>
    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools")))
            {
                return Path.Combine(dir.FullName, relative);
            }

            dir = dir.Parent;
        }

        return relative;
    }
}
