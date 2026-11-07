namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// Mod 库行三角提示的自检：判定优先级（未登记 → 库里状态 → 声明版本低于当前游戏版本）、
/// 版本对照结论（未声明 / 游戏版本未知 / 不低于 / 低于），以及文案键确实存在于两份 modlib.json 里
/// （Core 引用不到 App 生成的 Keys 常量，键名一旦打错界面只会显示 ⟨key⟩，只能这样兜住）。
/// 不联网、不启动任何进程。
/// </summary>
public static class ModLibraryWarningChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckUnregistered();
        CheckStatus();
        CheckVersionBelow();
        CheckVersionNotBelow();
        CheckUndeclared();
        CheckTextKeysExist();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>造一行 Mod 库数据（默认值按官方库常见形态填）。</summary>
    private static ModLibraryEntry Entry(
        string requiredVersion = "",
        string status = "ready_to_play",
        bool isUnregistered = false,
        string? steamId = null,
        string gameRegistryId = "ck3")
        => new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            "测试 mod",
            gameRegistryId,
            "C:/mods/test",
            null,
            null,
            requiredVersion,
            status,
            steamId is null ? "local" : "steam",
            null,
            null,
            Array.Empty<Guid>(),
            isUnregistered,
            steamId);

    /// <summary>磁盘扫到、库里没登记的行：本地 mod 与工坊订阅要分开。</summary>
    private static void CheckUnregistered()
    {
        var local = ModLibraryWarnings.Resolve(Entry(isUnregistered: true), "1.13.11 (Matcha)");
        ChecksContext.Equal("未登记本地 mod：类别", ModWarningKind.Unregistered, local.Kind);
        ChecksContext.Equal("未登记本地 mod：补充说明键", ModLibraryWarnings.KeyUnregistered, local.NoteKey);
        ChecksContext.Check("未登记本地 mod：有三角", local.HasTriangle);
        ChecksContext.Equal("未登记本地 mod：没有对照结论", null, local.ConclusionKey);

        var workshop = ModLibraryWarnings.Resolve(
            Entry(isUnregistered: true, steamId: "2216623537"),
            "1.13.11 (Matcha)");
        ChecksContext.Equal("未登记工坊 mod：类别", ModWarningKind.UnregisteredWorkshop, workshop.Kind);
        ChecksContext.Equal(
            "未登记工坊 mod：补充说明键",
            ModLibraryWarnings.KeyUnregisteredWorkshop,
            workshop.NoteKey);
    }

    /// <summary>mods.status 不是 ready_to_play 时，第三句说明库里状态。</summary>
    private static void CheckStatus()
    {
        var missing = ModLibraryWarnings.Resolve(Entry(status: "missing"), "1.13.11 (Matcha)");
        ChecksContext.Equal("状态不是 ready_to_play：类别", ModWarningKind.Status, missing.Kind);
        ChecksContext.Equal("状态不是 ready_to_play：补充说明键", ModLibraryWarnings.KeyStatus, missing.NoteKey);
        ChecksContext.Equal("状态不是 ready_to_play：状态原文", "missing", missing.Status);
        ChecksContext.Equal("状态不是 ready_to_play：补充说明要 1 个参数", 1, missing.NoteArgumentCount);

        // 大小写不敏感：官方库里大小写不统一时也应算异常状态。
        var otherCase = ModLibraryWarnings.Resolve(Entry(status: "MISSING"), null);
        ChecksContext.Equal("状态大小写不同：也算状态类", ModWarningKind.Status, otherCase.Kind);

        // 反过来，ready_to_play 的不同写法不算状态问题。
        var readyCase = ModLibraryWarnings.Resolve(Entry(status: "Ready_To_Play"), null);
        ChecksContext.Equal("ready_to_play 的不同写法：不算状态类", ModWarningKind.None, readyCase.Kind);
    }

    /// <summary>声明版本低于当前游戏版本 → 挂三角，并给出「低于」的结论。</summary>
    private static void CheckVersionBelow()
    {
        var below = ModLibraryWarnings.Resolve(Entry("1.12.*"), "1.13.11 (Matcha)");
        ChecksContext.Equal("声明版本低于当前版本：类别", ModWarningKind.VersionBelow, below.Kind);
        ChecksContext.Equal("声明版本低于当前版本：结论", ModVersionSupport.Below, below.VersionSupport);
        ChecksContext.Equal("声明版本低于当前版本：结论键", ModLibraryWarnings.KeyVersionBelow, below.ConclusionKey);
        ChecksContext.Equal("声明版本低于当前版本：版本说明键", ModLibraryWarnings.KeyVersionDeclared, below.VersionKey);
        ChecksContext.Equal("声明版本低于当前版本：版本说明要 2 个参数", 2, below.VersionArgumentCount);
        ChecksContext.Equal("声明版本低于当前版本：声明版本原文", "1.12.*", below.RequiredVersion);
        ChecksContext.Equal("声明版本低于当前版本：游戏版本原文", "1.13.11 (Matcha)", below.GameVersion);
        ChecksContext.Equal("声明版本低于当前版本：没有补充说明", null, below.NoteKey);
    }

    /// <summary>声明版本不低于当前游戏版本 → 不挂三角，但版本对照结论仍可读。</summary>
    private static void CheckVersionNotBelow()
    {
        var same = ModLibraryWarnings.Resolve(Entry("1.13.*"), "1.13.11 (Matcha)");
        ChecksContext.Equal("声明版本不低于当前版本：不挂三角", ModWarningKind.None, same.Kind);
        ChecksContext.Check("声明版本不低于当前版本：没有三角", !same.HasTriangle);
        ChecksContext.Equal("声明版本不低于当前版本：结论", ModVersionSupport.NotBelow, same.VersionSupport);

        var higher = ModLibraryWarnings.Resolve(Entry("1.20.*"), "1.13.11 (Matcha)");
        ChecksContext.Equal("声明版本高于当前版本：不挂三角", ModWarningKind.None, higher.Kind);
        ChecksContext.Equal("声明版本高于当前版本：结论", ModVersionSupport.NotBelow, higher.VersionSupport);
    }

    /// <summary>没声明版本 / 游戏版本未知：不下「低于」的结论，也不挂三角。</summary>
    private static void CheckUndeclared()
    {
        var empty = ModLibraryWarnings.Resolve(Entry(""), "1.13.11 (Matcha)");
        ChecksContext.Equal("空 requiredVersion：未声明", ModVersionSupport.Undeclared, empty.VersionSupport);
        ChecksContext.Equal("空 requiredVersion：不挂三角", ModWarningKind.None, empty.Kind);

        var wildcard = ModLibraryWarnings.Resolve(Entry("1.*.*"), "1.13.11 (Matcha)");
        ChecksContext.Equal("1.*.*：未声明", ModVersionSupport.Undeclared, wildcard.VersionSupport);
        ChecksContext.Equal("1.*.*：不挂三角", ModWarningKind.None, wildcard.Kind);

        var unknownGame = ModLibraryWarnings.Resolve(Entry("1.13.*"), null);
        ChecksContext.Equal("游戏版本未知：结论", ModVersionSupport.DeclaredUnknownGame, unknownGame.VersionSupport);
        ChecksContext.Equal("游戏版本未知：不挂三角", ModWarningKind.None, unknownGame.Kind);

        var unknownGameEmpty = ModLibraryWarnings.Resolve(Entry("1.13.*"), "");
        ChecksContext.Equal("游戏版本为空：结论", ModVersionSupport.DeclaredUnknownGame, unknownGameEmpty.VersionSupport);

        // 未登记的行即使版本对得上也要提示（登记情况优先）。
        var unregistered = ModLibraryWarnings.Resolve(Entry("1.13.*", isUnregistered: true), "1.13.11 (Matcha)");
        ChecksContext.Equal("未登记优先于版本结论", ModWarningKind.Unregistered, unregistered.Kind);
    }

    /// <summary>文案键必须能在两份 modlib.json 里找到（键名写错时界面只会显示 ⟨key⟩）。</summary>
    private static void CheckTextKeysExist()
    {
        string[] keys =
        [
            ModLibraryWarnings.NameKey,
            ModLibraryWarnings.KeyVersionUndeclared,
            ModLibraryWarnings.KeyVersionDeclaredUnknownGame,
            ModLibraryWarnings.KeyVersionDeclared,
            ModLibraryWarnings.KeyVersionNotBelow,
            ModLibraryWarnings.KeyVersionBelow,
            ModLibraryWarnings.KeyUnregistered,
            ModLibraryWarnings.KeyUnregisteredWorkshop,
            ModLibraryWarnings.KeyStatus,
        ];

        string[] languages = ["zh-CN", "en-US"];
        foreach (var language in languages)
        {
            var path = I18nResw.ResourcesPath(I18nResw.RepoRoot(), language);
            if (!File.Exists(path))
            {
                ChecksContext.Check("文案表存在：" + language, false, path);
                continue;
            }

            var table = I18nResw.Read(I18nResw.RepoRoot(), language);
            foreach (var key in keys)
            {
                var found = I18nResw.Get(table, key) is not null;
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
