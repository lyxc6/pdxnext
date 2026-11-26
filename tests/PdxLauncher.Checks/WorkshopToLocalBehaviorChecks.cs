namespace PdxLauncher.Checks;

using System.Text.Json.Nodes;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 「工坊 mod 转本地 mod」的 Core 行为自检（由 Program.cs 反射发现）：
/// 全部在临时目录里跑真实的 <see cref="WorkshopToLocalConverter"/> + 真实 sqlite 仓储 +
/// 真实 <see cref="FileSystem"/>，只在需要注入故障时用装饰器 / 包装器。
/// <para>
/// 覆盖 legacy 外层描述符改写、metadata 数字 id 清空、播放集多引用逐一替换、
/// 写库失败回滚、以及「反向补偿也失败」时磁盘副本与库状态的一致性。
/// 与 <see cref="WorkshopToLocalUiChecks"/> 的源码文本断言互补。
/// </para>
/// </summary>
public static class WorkshopToLocalBehaviorChecks
{
    private const string StagingFolderName = ".pdxnext-staging";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        RunCase("legacy 描述符改写", () => LegacyDescriptorRewrite(fs));
        RunCase("中文 / 空格目录名与撞名", () => LocalizedFolderNames(fs));
        RunCase("内层 descriptor.mod 去工坊 id", () => InnerDescriptorWorkshopIdCleared(fs));
        RunCase("metadata 数字 id 清空", () => MetadataIdCleared(fs));
        RunCase("归档落地与 archive 路径", () => ArchiveLanding(fs));
        RunCase("播放集多引用替换", () => MultiReferenceAdoption(fs));
        RunCase("AdoptLocalMod 失败回滚", () => AdoptFailureRollback(fs));
        RunCase("反向补偿失败保留磁盘", () => RevertFailureKeepsDisk(fs));
        RunCase("反向补偿成功删除磁盘", () => RevertSuccessDeletesDisk(fs));
        RunCase("CopyDirectory 取消令牌", () => CopyDirectoryCancellation(fs));

        return ChecksContext.Failures - before;
    }

    private static void RunCase(string label, Func<Task> body)
    {
        try
        {
            body().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            ChecksContext.Check("工坊转本地行为: " + label + " 未抛异常", false, exception.ToString());
        }
    }

    // ---------------------------------------------------------------- legacy

    private static async Task LegacyDescriptorRewrite(IFileSystem fs)
    {
        using var f = new Fixture(fs, "legacy", "ck3", LoadFileFormat.DlcLoadV0);
        const string source = "name=\"Legacy Test\"\n"
            + "version=\"1.2.3\"\n"
            + "supported_version=\"1.9.*\"\n"
            + "tags={ \"Utility\" \"UI\" }\n"
            + "picture=\"preview.png\"\n"
            + "remote_file_id=\"12345\"\n"
            + "path=\"steamapps/workshop/content/999/12345\"\n";
        f.WriteSource("descriptor.mod", source);
        f.WriteSource("content.txt", "hello-from-workshop");

        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Legacy Test", "1.9.*", "1.2.3");
        await f.SeedAsync(old).ConfigureAwait(true);
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        var result = await f.MakeConverter().ConvertToLocalAsync(
            f.Game, oldId, "LegacyTestMod", null, CancellationToken.None).ConfigureAwait(true);
        if (!ChecksContext.Check("legacy: 转换成功", result.IsSuccess, result.ErrorMessage))
        {
            return;
        }

        var outer = Path.Combine(f.Game.ModDir, "LegacyTestMod.mod");
        var contentDir = Path.Combine(f.Game.ModDir, "LegacyTestMod");
        ChecksContext.Check("legacy: 顶层外层 .mod 已写出", File.Exists(outer));
        var text = File.Exists(outer) ? File.ReadAllText(outer) : string.Empty;
        ChecksContext.Check("legacy: name 保留", text.Contains("name=\"Legacy Test\"", StringComparison.Ordinal));
        ChecksContext.Check("legacy: version 保留", text.Contains("version=\"1.2.3\"", StringComparison.Ordinal));
        ChecksContext.Check("legacy: supported_version 保留", text.Contains("supported_version=\"1.9.*\"", StringComparison.Ordinal));
        ChecksContext.Check("legacy: tags 保留", text.Contains("\"Utility\"", StringComparison.Ordinal)
            && text.Contains("\"UI\"", StringComparison.Ordinal));
        ChecksContext.Check("legacy: picture 保留", text.Contains("picture=\"preview.png\"", StringComparison.Ordinal));
        ChecksContext.Check("legacy: remote_file_id 已删", !text.Contains("remote_file_id", StringComparison.Ordinal));

        var expectedPath = Path.GetFullPath(contentDir).Replace('\\', '/');
        ChecksContext.Check("legacy: path 改写为最终绝对路径",
            text.Contains("path=\"" + expectedPath + "\"", StringComparison.Ordinal), expectedPath);
        ChecksContext.Check("legacy: 目录内容完整复制", File.Exists(Path.Combine(contentDir, "content.txt")));
        ChecksContext.Check("legacy: 内层 descriptor.mod 原样保留", File.Exists(Path.Combine(contentDir, "descriptor.mod")));

        var library = await f.Repo.GetModLibraryAsync(f.Game, CancellationToken.None).ConfigureAwait(true);
        var local = library.FirstOrDefault(x => x.ModId == result.Value!.NewModId);
        ChecksContext.Check("legacy: 新行 source=local / steamId 为空",
            local is not null && local.Source == ModOrigins.LocalSource && local.SteamId is null);
    }

    // --------------------------------------------------------------- metadata

    /// <summary>
    /// 中文 / 带空格的名字必须原样落地，不能都折成 <c>localmod</c>；
    /// 第二个中文名撞名时按 <c>_2</c> 递进，且大小写不同也算撞名（Windows 语义）。
    /// </summary>
    private static async Task LocalizedFolderNames(IFileSystem fs)
    {
        using var f = new Fixture(fs, "localized", "ck3", LoadFileFormat.DlcLoadV0);
        f.WriteSource("descriptor.mod", "name=\"Localized\"\nversion=\"1.0\"\nsupported_version=\"1.9.*\"\n");
        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Localized", "1.9.*", "1.0");
        await f.SeedAsync(old).ConfigureAwait(true);
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        var first = await f.MakeConverter().ConvertToLocalAsync(
            f.Game, oldId, "苦难的女囚", null, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("中文名: 转换成功", first.IsSuccess, first.ErrorMessage);
        ChecksContext.Check(
            "中文名: 落地目录叫原名（苦难的女囚）",
            Directory.Exists(Path.Combine(f.Game.ModDir, "苦难的女囚")));
        ChecksContext.Check(
            "中文名: 顶层外层 .mod 叫原名（苦难的女囚.mod）",
            File.Exists(Path.Combine(f.Game.ModDir, "苦难的女囚.mod")));
        foreach (var legacy in new[] { "localmod", "localmod.mod", "localmod.zip" })
        {
            ChecksContext.Check(
                "中文名: 不再落到 localmod（" + legacy + "）",
                !File.Exists(Path.Combine(f.Game.ModDir, legacy))
                    && !Directory.Exists(Path.Combine(f.Game.ModDir, legacy)));
        }

        if (first.IsSuccess)
        {
            ChecksContext.Check(
                "中文名: path 指向中文目录",
                File.ReadAllText(Path.Combine(f.Game.ModDir, "苦难的女囚.mod"))
                    .Contains("path=\"" + Path.GetFullPath(Path.Combine(f.Game.ModDir, "苦难的女囚")).Replace('\\', '/') + "\"",
                        StringComparison.Ordinal));
        }

        // 第二个不同 mod 用同一个展示名（如作者改名前后的两份订阅）：目录名必须变成 苦难的女囚_2，不能覆盖已有目录。
        var again = f.MakeOldMod(Guid.NewGuid(), "Localized", "1.9.*", "1.0");
        await f.SeedAsync(again).ConfigureAwait(true);
        f.Catalog.Mod = again with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };
        var second = await f.MakeConverter().ConvertToLocalAsync(
            f.Game, again.ModId, "苦难的女囚", null, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("中文名撞名: 第二次转换成功", second.IsSuccess, second.ErrorMessage);
        ChecksContext.Check(
            "中文名撞名: 落地为 苦难的女囚_2",
            Directory.Exists(Path.Combine(f.Game.ModDir, "苦难的女囚_2"))
                && File.Exists(Path.Combine(f.Game.ModDir, "苦难的女囚_2.mod")));

        // 大小写不同的同名条目也要避让（Windows 上没法共存两个只差大小写的目录）。
        Directory.CreateDirectory(Path.Combine(f.Game.ModDir, "CaseMod"));
        var third = await f.MakeConverter().ConvertToLocalAsync(
            f.Game, again.ModId, "CASEMOD", null, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check(
            "大小写撞名: 不落到 CaseMod，改走 CASEMOD_2",
            !third.IsSuccess || Directory.Exists(Path.Combine(f.Game.ModDir, "CASEMOD_2")),
            third.ErrorMessage);
    }

    /// <summary>目录型 legacy 的内层 <c>descriptor.mod</c> 必须去掉 <c>remote_file_id</c>，否则第三方读到的仍是工坊 mod。</summary>
    private static async Task InnerDescriptorWorkshopIdCleared(IFileSystem fs)
    {
        using var f = new Fixture(fs, "inner", "ck3", LoadFileFormat.DlcLoadV0);
        f.WriteSource(
            "descriptor.mod",
            "version=\"1.0\"\ntags={ \"Gameplay\" }\nname=\"Inner Test\"\n"
            + "supported_version=\"1.9.*\"\nremote_file_id=\"12345\"\n");
        f.WriteSource("content.txt", "payload");

        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Inner Test", "1.9.*", "1.0");
        await f.SeedAsync(old).ConfigureAwait(true);
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        var result = await f.MakeConverter().ConvertToLocalAsync(
            f.Game, oldId, "InnerMod", null, CancellationToken.None).ConfigureAwait(true);
        if (!ChecksContext.Check("内层描述符: 转换成功", result.IsSuccess, result.ErrorMessage))
        {
            return;
        }

        var inner = Path.Combine(f.Game.ModDir, "InnerMod", "descriptor.mod");
        var text = File.ReadAllText(inner);
        ChecksContext.Check("内层描述符: remote_file_id 已删", !text.Contains("remote_file_id", StringComparison.Ordinal));
        ChecksContext.Check("内层描述符: name 仍保留", text.Contains("name=\"Inner Test\"", StringComparison.Ordinal));
        ChecksContext.Check("内层描述符: tags 仍保留", text.Contains("\"Gameplay\"", StringComparison.Ordinal));
    }

    private static async Task MetadataIdCleared(IFileSystem fs)
    {
        using var f = new Fixture(fs, "metadata", "victoria3", LoadFileFormat.ContentLoadV1_1);
        var metadataDir = Path.Combine(f.WorkshopDir, ".metadata");
        Directory.CreateDirectory(metadataDir);
        File.WriteAllText(
            Path.Combine(metadataDir, "metadata.json"),
            "{\n  \"id\": \"12345\",\n  \"name\": \"Metadata Test\",\n  \"version\": \"2.0\"\n}\n");

        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Metadata Test", "1.11.*", "2.0");
        await f.SeedAsync(old).ConfigureAwait(true);
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        var result = await f.MakeConverter().ConvertToLocalAsync(
            f.Game, oldId, "MetadataTestMod", null, CancellationToken.None).ConfigureAwait(true);
        if (!ChecksContext.Check("metadata: 转换成功", result.IsSuccess, result.ErrorMessage))
        {
            return;
        }

        var metaPath = Path.Combine(f.Game.ModDir, "MetadataTestMod", ".metadata", "metadata.json");
        ChecksContext.Check("metadata: 落地副本含 metadata.json", File.Exists(metaPath));
        if (!File.Exists(metaPath))
        {
            return;
        }

        var json = JsonNode.Parse(File.ReadAllText(metaPath)) as JsonObject;
        ChecksContext.Check("metadata: 数字 id 已清空为空串",
            json is not null && json["id"] is not null && json["id"]!.ToJsonString().Trim('"').Length == 0);
        ChecksContext.Check("metadata: 其余键保留",
            json is not null
            && json["name"]?.ToJsonString().Trim('"') == "Metadata Test"
            && json["version"]?.ToJsonString().Trim('"') == "2.0");
        ChecksContext.Check("metadata: 不写 legacy 外层 .mod",
            !File.Exists(Path.Combine(f.Game.ModDir, "MetadataTestMod.mod")));
    }

    // ----------------------------------------------------------- 播放集引用

    private static async Task ArchiveLanding(IFileSystem fs)
    {
        using var f = new Fixture(fs, "archive", "ck3", LoadFileFormat.ContentLoadV1);
        f.WriteSource("descriptor.mod", "name=\"Archive Test\"\nversion=\"1.0\"\nsupported_version=\"1.9.*\"\n");
        f.WriteSource("data.txt", "packed-from-workshop");
        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Archive Test", "1.9.*", "1.0");
        await f.SeedAsync(old).ConfigureAwait(true);
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        var result = await f.MakeConverter().ConvertToLocalAsync(
            f.Game, oldId, "ArchiveTestMod", null, CancellationToken.None).ConfigureAwait(true);
        if (!ChecksContext.Check("归档: 转换成功", result.IsSuccess, result.ErrorMessage))
        {
            return;
        }

        var zip = Path.Combine(f.Game.ModDir, "ArchiveTestMod.zip");
        ChecksContext.Check("归档: 现场打包 zip 落地", File.Exists(zip));
        ChecksContext.Check("归档: 不落地同名目录", !Directory.Exists(Path.Combine(f.Game.ModDir, "ArchiveTestMod")));
        var outer = Path.Combine(f.Game.ModDir, "ArchiveTestMod.mod");
        ChecksContext.Check("归档: 外层 .mod 写出", File.Exists(outer));
        if (!File.Exists(outer))
        {
            return;
        }

        var text = File.ReadAllText(outer);
        var expected = Path.GetFullPath(zip).Replace('\\', '/');
        ChecksContext.Check("归档: archive= 指向最终绝对 zip 路径",
            text.Contains("archive=\"" + expected + "\"", StringComparison.Ordinal), expected);
        ChecksContext.Check("归档: 不写 path=", !text.Contains("path=", StringComparison.Ordinal));
        ChecksContext.Check("归档: 结果带回 ArchivePath",
            string.Equals(result.Value!.ArchivePath, zip, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task MultiReferenceAdoption(IFileSystem fs)
    {
        using var f = new Fixture(fs, "refs", "ck3", LoadFileFormat.DlcLoadV0);
        f.WriteSource("descriptor.mod", "name=\"Ref Test\"\nversion=\"1.0\"\nsupported_version=\"1.9.*\"\n");
        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Ref Test", "1.9.*", "1.0");

        // 两个播放集各引用一次：启用状态不同；其中一个前面再放一条 mod 使 position 不同。
        var dummy = f.MakeOldMod(Guid.NewGuid(), "Dummy", "1.9.*", "1.0");
        var setA = (await f.Repo.CreatePlaySetAsync(f.Game, "A", CancellationToken.None).ConfigureAwait(true)).Value;
        var setB = (await f.Repo.CreatePlaySetAsync(f.Game, "B", CancellationToken.None).ConfigureAwait(true)).Value;
        await f.Repo.SavePlaySetModsAsync(f.Game, setA, new[] { dummy, old with { Enabled = false } }, CancellationToken.None)
            .ConfigureAwait(true);
        await f.Repo.SavePlaySetModsAsync(f.Game, setB, new[] { old with { Enabled = true } }, CancellationToken.None)
            .ConfigureAwait(true);
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        var beforeA = (await f.Repo.GetPlaySetModsAsync(f.Game, setA, CancellationToken.None).ConfigureAwait(true))
            .First(x => x.ModId == oldId);
        var beforeB = (await f.Repo.GetPlaySetModsAsync(f.Game, setB, CancellationToken.None).ConfigureAwait(true))
            .First(x => x.ModId == oldId);
        ChecksContext.Equal("引用: 转换前 position 不同", 1, beforeA.Position);
        ChecksContext.Equal("引用: 转换前 position 为 0", 0, beforeB.Position);

        var result = await f.MakeConverter().ConvertToLocalAsync(
            f.Game, oldId, "RefTestMod", null, CancellationToken.None).ConfigureAwait(true);
        if (!ChecksContext.Check("引用: 转换成功", result.IsSuccess, result.ErrorMessage))
        {
            return;
        }

        var newId = result.Value!.NewModId;
        var afterA = (await f.Repo.GetPlaySetModsAsync(f.Game, setA, CancellationToken.None).ConfigureAwait(true))
            .FirstOrDefault(x => x.ModId == newId);
        var afterB = (await f.Repo.GetPlaySetModsAsync(f.Game, setB, CancellationToken.None).ConfigureAwait(true))
            .FirstOrDefault(x => x.ModId == newId);
        ChecksContext.Check("引用: 播放集 A 换成新 ModId", afterA is not null);
        ChecksContext.Check("引用: 播放集 B 换成新 ModId", afterB is not null);
        ChecksContext.Check("引用: enabled 逐一保留",
            afterA is not null && afterA.Enabled == beforeA.Enabled
            && afterB is not null && afterB.Enabled == beforeB.Enabled);
        ChecksContext.Check("引用: position 逐一保留",
            afterA is not null && afterA.Position == beforeA.Position
            && afterB is not null && afterB.Position == beforeB.Position);

        var oldStillA = (await f.Repo.GetPlaySetModsAsync(f.Game, setA, CancellationToken.None).ConfigureAwait(true))
            .Any(x => x.ModId == oldId);
        var oldStillB = (await f.Repo.GetPlaySetModsAsync(f.Game, setB, CancellationToken.None).ConfigureAwait(true))
            .Any(x => x.ModId == oldId);
        ChecksContext.Check("引用: 旧 ModId 不再被引用", !oldStillA && !oldStillB);
    }

    // -------------------------------------------------------------- 失败回滚

    private static async Task AdoptFailureRollback(IFileSystem fs)
    {
        using var f = new Fixture(fs, "adopt-fail", "ck3", LoadFileFormat.DlcLoadV0);
        f.WriteSource("descriptor.mod", "name=\"Fail Test\"\nversion=\"1.0\"\nsupported_version=\"1.9.*\"\n");
        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Fail Test", "1.9.*", "1.0");
        var set = (await f.SeedAsync(old).ConfigureAwait(true))[0];
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        var decorator = new FaultRepository(f.Repo) { FailAdopt = true };
        var result = await f.MakeConverter(decorator).ConvertToLocalAsync(
            f.Game, oldId, "FailTestMod", null, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("回滚: AdoptLocalMod 失败返回 Io", !result.IsSuccess && result.Kind == ErrorKind.Io, result.ErrorMessage);
        ChecksContext.Check("回滚: 错误码透传仓储失败", string.Equals(result.ErrorMessage, "injected-adopt-failure", StringComparison.Ordinal));
        ChecksContext.Check("回滚: 落地目录已删除", !Directory.Exists(Path.Combine(f.Game.ModDir, "FailTestMod")));
        ChecksContext.Check("回滚: 顶层外层 .mod 已删除", !File.Exists(Path.Combine(f.Game.ModDir, "FailTestMod.mod")));
        ChecksContext.Check("回滚: staging 已清理", !StagingRootHasChildren(f.Game.ModDir));

        var mods = await f.Repo.GetPlaySetModsAsync(f.Game, set, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("回滚: 播放集引用仍是 oldModId", mods.Any(x => x.ModId == oldId));
        var library = await f.Repo.GetModLibraryAsync(f.Game, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("回滚: 没有 source=local 孤儿行", !library.Any(x => x.Source == ModOrigins.LocalSource));
    }

    private static async Task RevertFailureKeepsDisk(IFileSystem fs)
    {
        using var f = new Fixture(fs, "revert-fail", "ck3", LoadFileFormat.DlcLoadV0, ownModStoreFails: true);
        f.WriteSource("descriptor.mod", "name=\"Revert Fail\"\nversion=\"1.0\"\nsupported_version=\"1.9.*\"\n");
        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Revert Fail", "1.9.*", "1.0");
        var set = (await f.SeedAsync(old).ConfigureAwait(true))[0];
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        // 5b 抛异常 + 反向补偿失败：库仍指向新 mod，磁盘副本绝不能删，否则就是 missing 幽灵 mod。
        var decorator = new FaultRepository(f.Repo) { FailRevert = true };
        var result = await f.MakeConverter(decorator).ConvertToLocalAsync(
            f.Game, oldId, "RevertFailMod", null, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("双重失败: 返回失败且为 Io", !result.IsSuccess && result.Kind == ErrorKind.Io, result.ErrorMessage);
        ChecksContext.Check("双重失败: 落地目录保留", Directory.Exists(Path.Combine(f.Game.ModDir, "RevertFailMod")));
        ChecksContext.Check("双重失败: 顶层外层 .mod 保留", File.Exists(Path.Combine(f.Game.ModDir, "RevertFailMod.mod")));
        ChecksContext.Check("双重失败: staging 仍清理", !StagingRootHasChildren(f.Game.ModDir));

        var mods = await f.Repo.GetPlaySetModsAsync(f.Game, set, CancellationToken.None).ConfigureAwait(true);
        var localRefs = mods.Where(x => x.Source == ModOrigins.LocalSource && x.ModId != oldId).ToList();
        ChecksContext.Check("双重失败: 库引用与磁盘同在（无幽灵 mod）", localRefs.Count == 1);
    }

    private static async Task RevertSuccessDeletesDisk(IFileSystem fs)
    {
        using var f = new Fixture(fs, "revert-ok", "ck3", LoadFileFormat.DlcLoadV0, ownModStoreFails: true);
        f.WriteSource("descriptor.mod", "name=\"Revert Ok\"\nversion=\"1.0\"\nsupported_version=\"1.9.*\"\n");
        var oldId = Guid.NewGuid();
        var old = f.MakeOldMod(oldId, "Revert Ok", "1.9.*", "1.0");
        var set = (await f.SeedAsync(old).ConfigureAwait(true))[0];
        f.Catalog.Mod = old with { DirPath = f.WorkshopDir, Source = "steam", SteamId = "12345" };

        // 5b 抛异常但反向补偿成功：库已恢复，磁盘副本应删除。
        var decorator = new FaultRepository(f.Repo) { FailRevert = false };
        var result = await f.MakeConverter(decorator).ConvertToLocalAsync(
            f.Game, oldId, "RevertOkMod", null, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("补偿成功: 仍按原失败返回", !result.IsSuccess && result.Kind == ErrorKind.Io, result.ErrorMessage);
        ChecksContext.Check("补偿成功: 落地目录已删除", !Directory.Exists(Path.Combine(f.Game.ModDir, "RevertOkMod")));
        ChecksContext.Check("补偿成功: 顶层外层 .mod 已删除", !File.Exists(Path.Combine(f.Game.ModDir, "RevertOkMod.mod")));

        var mods = await f.Repo.GetPlaySetModsAsync(f.Game, set, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("补偿成功: 引用恢复为 oldModId", mods.Any(x => x.ModId == oldId));
        var library = await f.Repo.GetModLibraryAsync(f.Game, CancellationToken.None).ConfigureAwait(true);
        ChecksContext.Check("补偿成功: 没有 source=local 孤儿行", !library.Any(x => x.Source == ModOrigins.LocalSource));
    }

    private static Task CopyDirectoryCancellation(IFileSystem fs)
    {
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-w2l-copy-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var threw = false;
            try
            {
                fs.CopyDirectory(source, target, overwrite: true, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }

            ChecksContext.Check("取消: CopyDirectory 在入口即响应已取消令牌", threw);
            ChecksContext.Check("取消: 目标目录 remain empty（未复制文件）",
                !Directory.Exists(target) || !Directory.EnumerateFiles(target).Any());
        }
        finally
        {
            TryDelete(root);
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ 辅助

    /// <summary>测试夹具：独立临时目录 + 真实仓储 / 真实自有库 / 真实文件系统。</summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture(IFileSystem fs, string label, string gameId, LoadFileFormat format, bool ownModStoreFails = false)
        {
            Root = Path.Combine(Path.GetTempPath(), "pdxnext-w2l-" + label + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Logger = new NullLogger();
            Clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var data = Path.Combine(Root, "documents", gameId);
            Directory.CreateDirectory(data);
            ModDir = Path.Combine(data, "mod");
            Directory.CreateDirectory(ModDir);
            Game = PlaysetChecks.CreateGame(
                gameId, Path.Combine(Root, "install", gameId), format, data,
                Path.Combine(Root, "install", gameId, "dlc"), ModDir, steamAppId: null);

            WorkshopDir = Path.Combine(Root, "steamapps", "workshop", "content", "999", "12345");
            Directory.CreateDirectory(WorkshopDir);

            Repo = new OwnDatabasePlaySetRepository(
                new PdxDatabase(fs, Logger, Path.Combine(Root, "repo.sqlite")), Logger, Clock);

            if (ownModStoreFails)
            {
                // 让 UpsertMods 抛 IOException：数据库路径的父级是已存在的文件，EnsureDirectory 必失败。
                var blocker = Path.Combine(Root, "ownmod-blocker");
                File.WriteAllText(blocker, "not a directory");
                OwnMods = new OwnModStore(
                    new PdxDatabase(fs, Logger, Path.Combine(blocker, "own.sqlite")), Clock, Logger);
            }
            else
            {
                OwnMods = new OwnModStore(
                    new PdxDatabase(fs, Logger, Path.Combine(Root, "ownmods.sqlite")), Clock, Logger);
            }

            Fs = fs;
            Catalog = new FixedCatalog();
            Invalidator = new NoopInvalidator();
        }

        public string Root { get; }

        public IFileSystem Fs { get; }

        public NullLogger Logger { get; }

        public FixedClock Clock { get; }

        public GameInstallation Game { get; }

        public string ModDir { get; }

        public string WorkshopDir { get; }

        public OwnDatabasePlaySetRepository Repo { get; }

        public OwnModStore OwnMods { get; }

        public FixedCatalog Catalog { get; }

        public NoopInvalidator Invalidator { get; }

        public void WriteSource(string name, string text) => File.WriteAllText(Path.Combine(WorkshopDir, name), text);

        public PlaySetMod MakeOldMod(Guid id, string name, string required, string version, bool enabled = true) =>
            new(id, name, WorkshopDir, null, string.Empty, null, required, "ready_to_play", enabled, 0,
                SteamId: "12345", Source: ModOrigins.SteamSource, ModVersion: version);

        public async Task<Guid[]> SeedAsync(params PlaySetMod[] mods)
        {
            var ids = new List<Guid>();
            for (var i = 0; i < mods.Length; i++)
            {
                var id = (await Repo.CreatePlaySetAsync(Game, "Set" + i, CancellationToken.None).ConfigureAwait(true)).Value;
                ids.Add(id);
                var saved = await Repo.SavePlaySetModsAsync(Game, id, new[] { mods[i] }, CancellationToken.None).ConfigureAwait(true);
                ChecksContext.Check("夹具: 播放集 " + i + " 播种成功", saved.IsSuccess, saved.ErrorMessage);
            }

            return ids.ToArray();
        }

        public WorkshopToLocalConverter MakeConverter(IPlaySetRepository? repo = null) => new(
            Fs,
            repo ?? Repo,
            Catalog,
            Invalidator,
            new ModArchivePackager(),
            OwnMods,
            Logger,
            Clock);

        public void Dispose() => TryDelete(Root);
    }

    /// <summary>staging 根目录允许留着（下次复用），但本次转换的暂存子目录必须清空。</summary>
    private static bool StagingRootHasChildren(string modDir)
    {
        var root = Path.Combine(modDir, StagingFolderName);
        return Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FixedCatalog : IModCatalog
    {
        public PlaySetMod? Mod { get; set; }

        public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySetMod>>(Mod is null ? Array.Empty<PlaySetMod>() : new[] { Mod });
    }

    private sealed class NoopInvalidator : IScanCacheInvalidator
    {
        public void InvalidateCache(GameInstallation game)
        {
        }

        public void ClearCache()
        {
        }
    }

    /// <summary>用真实仓储做读写，只在 Adopt / Revert 上按开关返回稳定失败。</summary>
    private sealed class FaultRepository : IPlaySetRepository
    {
        private readonly IPlaySetRepository _inner;

        public FaultRepository(IPlaySetRepository inner) => _inner = inner;

        public bool FailAdopt { get; set; }

        public bool FailRevert { get; set; }

        public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken) =>
            _inner.GetPlaySetsAsync(game, cancellationToken);

        public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken) =>
            _inner.GetActivePlaySetAsync(game, cancellationToken);

        public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            _inner.GetPlaySetModsAsync(game, playSetId, cancellationToken);

        public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(GameInstallation game, CancellationToken cancellationToken) =>
            _inner.GetModLibraryAsync(game, cancellationToken);

        public Task<AppResult<int>> RemoveWorkshopModAsync(GameInstallation game, string workshopId, CancellationToken cancellationToken) =>
            _inner.RemoveWorkshopModAsync(game, workshopId, cancellationToken);

        public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            _inner.GetPlaySetDlcsAsync(game, playSetId, cancellationToken);

        public Task<AppResult<bool>> UpdateModPathsAsync(GameInstallation game, Guid modId, string? dirPath, string? archivePath, CancellationToken cancellationToken) =>
            _inner.UpdateModPathsAsync(game, modId, dirPath, archivePath, cancellationToken);

        public Task<AppResult<bool>> UpdateModRegistryIdAsync(GameInstallation game, Guid modId, string? gameRegistryId, CancellationToken cancellationToken) =>
            _inner.UpdateModRegistryIdAsync(game, modId, gameRegistryId, cancellationToken);

        public Task<AppResult<bool>> AdoptLocalModAsync(GameInstallation game, Guid oldModId, string? oldDirPath, string? oldArchivePath, PlaySetMod localMod, CancellationToken cancellationToken) =>
            FailAdopt
                ? Task.FromResult(AppResult<bool>.Fail(ErrorKind.Io, "injected-adopt-failure"))
                : _inner.AdoptLocalModAsync(game, oldModId, oldDirPath, oldArchivePath, localMod, cancellationToken);

        public Task<AppResult<bool>> RevertLocalModAdoptionAsync(GameInstallation game, Guid newModId, Guid oldModId, string? oldDirPath, string? oldArchivePath, CancellationToken cancellationToken, string? newDirPath = null, string? newArchivePath = null) =>
            FailRevert
                ? Task.FromResult(AppResult<bool>.Fail(ErrorKind.Io, "injected-revert-failure"))
                : _inner.RevertLocalModAdoptionAsync(game, newModId, oldModId, oldDirPath, oldArchivePath, cancellationToken, newDirPath, newArchivePath);

        public Task<AppResult<bool>> SavePlaySetModsAsync(GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetMod> mods, CancellationToken cancellationToken) =>
            _inner.SavePlaySetModsAsync(game, playSetId, mods, cancellationToken);

        public Task<AppResult<bool>> SavePlaySetDlcsAsync(GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetDlc> dlcs, CancellationToken cancellationToken) =>
            _inner.SavePlaySetDlcsAsync(game, playSetId, dlcs, cancellationToken);

        public Task<AppResult<Guid>> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken) =>
            _inner.CreatePlaySetAsync(game, name, cancellationToken);

        public Task<AppResult<bool>> SetActivePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            _inner.SetActivePlaySetAsync(game, playSetId, cancellationToken);

        public Task<AppResult<bool>> RenamePlaySetAsync(GameInstallation game, Guid playSetId, string newName, CancellationToken cancellationToken) =>
            _inner.RenamePlaySetAsync(game, playSetId, newName, cancellationToken);

        public Task<AppResult<bool>> DeletePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            _inner.DeletePlaySetAsync(game, playSetId, cancellationToken);
    }
}
