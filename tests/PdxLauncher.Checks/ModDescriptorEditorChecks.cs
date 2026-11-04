namespace PdxLauncher.Checks;

using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.ModStudio;

/// <summary>
/// 描述符编辑的自检：V3 的 metadata.json 要保留未知键，CK3 的 .mod 要保留注释/未知键/path，
/// 双文件写失败要回滚，损坏文件要拒绝而不是覆盖。
/// </summary>
public static class ModDescriptorEditorChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckMetadataPreservesUnknownKeys(fs);
        CheckMetadataRejectsInvalidJson(fs);
        CheckLegacyPreservesUnknownText(fs);
        CheckLegacyInsertsMissingKeys(fs);
        CheckInvalidNameWritesNothing(fs);
        CheckMissingDescriptor(fs);
        CheckLegacyRollback(fs);
        CheckLegacyQuotes(fs);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // V3 / metadata.json
    // ------------------------------------------------------------------

    private static void CheckMetadataPreservesUnknownKeys(IFileSystem fs)
    {
        Console.WriteLine("[ModDescriptorEditorChecks] metadata.json 保留未知键");
        var root = PlaysetChecks.CreateTempRoot(fs, "descriptor-metadata");
        try
        {
            var target = Path.Combine(root, "mod", "v3_mod");
            var metadataDir = Path.Combine(target, ".metadata");
            fs.EnsureDirectory(metadataDir);
            var path = Path.Combine(metadataDir, "metadata.json");
            fs.WriteAllTextAtomic(path, """
                {
                  "name": "Old Name",
                  "id": "12345",
                  "version": "0.0.1",
                  "supported_game_version": "1.7.*",
                  "short_description": "old",
                  "tags": ["A"],
                  "relationships": [{"relation": "dependency", "id": "99"}],
                  "game_custom_data": {"multiplayer_synchronized": true},
                  "custom_key": "keep me"
                }
                """);

            var info = MetadataInfo(path, "v3_mod", "Old Name", "0.0.1", "1.7.*", ["A"], "old");
            var result = ModDescriptorEditor.Save(
                info,
                new ModDescriptorEdit("New \"Name\" 中文", "2.0.0", "1.9.*", ["Economy", "Fixes"], "new desc"),
                fs);

            ChecksContext.Check("保存成功", result.Ok, result.ErrorCode ?? string.Empty);
            ChecksContext.Equal("写入路径", path, result.WrittenPaths.Count == 1 ? result.WrittenPaths[0] : "<none>");

            var updated = fs.ReadAllText(path);
            using var document = JsonDocument.Parse(updated);
            var rootElement = document.RootElement;
            ChecksContext.Equal("name 已更新", "New \"Name\" 中文", rootElement.GetProperty("name").GetString() ?? "<null>");
            ChecksContext.Equal("version 已更新", "2.0.0", rootElement.GetProperty("version").GetString() ?? "<null>");
            ChecksContext.Equal("supported_game_version 已更新", "1.9.*", rootElement.GetProperty("supported_game_version").GetString() ?? "<null>");
            ChecksContext.Equal("short_description 已更新", "new desc", rootElement.GetProperty("short_description").GetString() ?? "<null>");
            ChecksContext.Equal("tags 数量", 2, rootElement.GetProperty("tags").GetArrayLength());
            ChecksContext.Equal("tags[0]", "Economy", rootElement.GetProperty("tags")[0].GetString() ?? "<null>");
            ChecksContext.Equal("id 保留", "12345", rootElement.GetProperty("id").GetString() ?? "<null>");
            ChecksContext.Equal("relationships 保留", 1, rootElement.GetProperty("relationships").GetArrayLength());
            ChecksContext.Equal(
                "game_custom_data 保留",
                true,
                rootElement.GetProperty("game_custom_data").GetProperty("multiplayer_synchronized").GetBoolean());
            ChecksContext.Equal("未知键保留", "keep me", rootElement.GetProperty("custom_key").GetString() ?? "<null>");
            ChecksContext.Check("文件以单个 \\n 结尾", updated.EndsWith("\n", StringComparison.Ordinal) && !updated.Contains("\r\n", StringComparison.Ordinal));
            ChecksContext.Check("没有 UTF-8 BOM", updated.Length > 0 && updated[0] != '\uFEFF');
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckMetadataRejectsInvalidJson(IFileSystem fs)
    {
        Console.WriteLine("[ModDescriptorEditorChecks] metadata.json 损坏时不覆盖");
        var root = PlaysetChecks.CreateTempRoot(fs, "descriptor-badjson");
        try
        {
            var target = Path.Combine(root, "mod", "v3_mod");
            var metadataDir = Path.Combine(target, ".metadata");
            fs.EnsureDirectory(metadataDir);
            var path = Path.Combine(metadataDir, "metadata.json");
            const string broken = "{ \"name\": \"Broken\", ";
            fs.WriteAllTextAtomic(path, broken);

            var result = ModDescriptorEditor.Save(
                MetadataInfo(path, "v3_mod", "Broken", string.Empty, string.Empty, [], null),
                new ModDescriptorEdit("New", "1.0", "1.9.*", ["X"], "d"),
                fs);

            ChecksContext.Check("返回 invalid-json", !result.Ok && result.ErrorCode == "invalid-json", result.ErrorCode ?? "<null>");
            ChecksContext.Equal("文件内容未变", broken, fs.ReadAllText(path));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // CK3 / .mod
    // ------------------------------------------------------------------

    private static void CheckLegacyPreservesUnknownText(IFileSystem fs)
    {
        Console.WriteLine("[ModDescriptorEditorChecks] .mod 保留注释/未知键/path，并同步两个文件");
        var root = PlaysetChecks.CreateTempRoot(fs, "descriptor-legacy");
        try
        {
            var modRoot = Path.Combine(root, "mod");
            var target = Path.Combine(modRoot, "ck3_mod");
            fs.EnsureDirectory(target);
            var outer = Path.Combine(modRoot, "ck3_mod.mod");
            var inner = Path.Combine(target, "descriptor.mod");

            var innerText = """
                # 顶层注释
                name = "Old Name"  # 行内注释
                version = "0.0.1"
                tags = {
                    "A"
                    "B"
                }
                supported_version = "1.7.*"
                picture = "thumbnail.png"
                custom_key = "keep me"
                """;
            var outerText = innerText + "\npath = \"C:/mods/ck3_mod\"\n";
            fs.WriteAllTextAtomic(inner, innerText + "\n");
            fs.WriteAllTextAtomic(outer, outerText);

            var game = PlaysetChecks.CreateGame(
                "ck3",
                Path.Combine(root, "install"),
                LoadFileFormat.DlcLoadV0,
                Path.Combine(root, "documents"),
                Path.Combine(root, "install", "dlc"),
                modRoot,
                1158310);

            ChecksContext.Check(
                "读取旧式描述符",
                ModDescriptorReader.TryRead(game, target, "fallback", fs, out var before) && before is not null);
            ChecksContext.Equal("读取到外层 .mod", outer, before!.DescriptorPath ?? "<null>");

            var result = ModDescriptorEditor.Save(
                before,
                new ModDescriptorEdit("New Name", "2.0.0", "1.9.*", ["X", "Y"], "ignored"),
                fs);

            ChecksContext.Check("保存成功", result.Ok, result.ErrorCode ?? string.Empty);
            ChecksContext.Equal("两个文件都写入", 2, result.WrittenPaths.Count);

            var outerUpdated = fs.ReadAllText(outer);
            var innerUpdated = fs.ReadAllText(inner);

            foreach (var (label, text) in new[] { ("外层", outerUpdated), ("内层", innerUpdated) })
            {
                ChecksContext.Check(label + " 保留顶层注释", text.Contains("# 顶层注释", StringComparison.Ordinal));
                ChecksContext.Check(label + " 保留行内注释", text.Contains("# 行内注释", StringComparison.Ordinal));
                ChecksContext.Check(label + " 更新 name", text.Contains("name = \"New Name\"", StringComparison.Ordinal));
                ChecksContext.Check(label + " 更新 version", text.Contains("version = \"2.0.0\"", StringComparison.Ordinal));
                ChecksContext.Check(label + " 更新 supported_version", text.Contains("supported_version = \"1.9.*\"", StringComparison.Ordinal));
                ChecksContext.Check(label + " 重写 tags", text.Contains("tags = {\"X\" \"Y\"}", StringComparison.Ordinal));
                ChecksContext.Check(label + " 不再包含旧标签", !text.Contains("\"A\"", StringComparison.Ordinal));
                ChecksContext.Check(label + " 保留 picture", text.Contains("picture = \"thumbnail.png\"", StringComparison.Ordinal));
                ChecksContext.Check(label + " 保留未知键", text.Contains("custom_key = \"keep me\"", StringComparison.Ordinal));
            }

            ChecksContext.Check("外层保留 path", outerUpdated.Contains("path = \"C:/mods/ck3_mod\"", StringComparison.Ordinal));
            ChecksContext.Check("内层不写 path", !innerUpdated.Contains("path =", StringComparison.Ordinal));

            ChecksContext.Check(
                "回读包含新标签",
                ModDescriptorReader.TryRead(game, target, "fallback", fs, out var after) && after is not null);
            ChecksContext.Equal("回读 name", "New Name", after!.Name);
            ChecksContext.Equal("回读 version", "2.0.0", after.Version);
            ChecksContext.Equal("回读 supported_version", "1.9.*", after.SupportedVersion);
            ChecksContext.Equal("回读标签数", 2, after.Tags.Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckLegacyInsertsMissingKeys(IFileSystem fs)
    {
        Console.WriteLine("[ModDescriptorEditorChecks] .mod 缺键时补在 name 之后");
        var root = PlaysetChecks.CreateTempRoot(fs, "descriptor-missing-keys");
        try
        {
            var modRoot = Path.Combine(root, "mod");
            var target = Path.Combine(modRoot, "ck3_mod");
            fs.EnsureDirectory(target);
            var outer = Path.Combine(modRoot, "ck3_mod.mod");
            fs.WriteAllTextAtomic(outer, "version = \"0.1\"\npicture = \"thumb.png\"\n");

            var info = LegacyInfo(outer, "ck3_mod", "ck3_mod", string.Empty, "0.1", string.Empty, [], null);
            var result = ModDescriptorEditor.Save(
                info,
                new ModDescriptorEdit("Inserted", "2.0", "1.9.*", ["T"], null),
                fs);

            ChecksContext.Check("保存成功", result.Ok, result.ErrorCode ?? string.Empty);
            var text = fs.ReadAllText(outer);
            ChecksContext.Check("name 在文件开头", text.StartsWith("name=", StringComparison.Ordinal), text.Split('\n')[0]);
            ChecksContext.Check("补上 supported_version", text.Contains("supported_version=\"1.9.*\"", StringComparison.Ordinal));
            ChecksContext.Check("补上 tags", text.Contains("tags={\"T\"}", StringComparison.Ordinal));
            ChecksContext.Check("保留 version", text.Contains("version = \"2.0\"", StringComparison.Ordinal));
            ChecksContext.Check("保留 picture", text.Contains("picture = \"thumb.png\"", StringComparison.Ordinal));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 校验与失败路径
    // ------------------------------------------------------------------

    private static void CheckInvalidNameWritesNothing(IFileSystem fs)
    {
        Console.WriteLine("[ModDescriptorEditorChecks] 空名称不产生写入");
        var root = PlaysetChecks.CreateTempRoot(fs, "descriptor-invalid-name");
        try
        {
            var modRoot = Path.Combine(root, "mod");
            var target = Path.Combine(modRoot, "ck3_mod");
            fs.EnsureDirectory(target);
            var outer = Path.Combine(modRoot, "ck3_mod.mod");
            const string original = "name=\"Old\"\nversion=\"0.1\"\n";
            fs.WriteAllTextAtomic(outer, original);

            var result = ModDescriptorEditor.Save(
                LegacyInfo(outer, "ck3_mod", "ck3_mod", "Old", "0.1", string.Empty, [], null),
                new ModDescriptorEdit("   ", "2.0", string.Empty, [], null),
                fs);

            ChecksContext.Check("返回 invalid-name", !result.Ok && result.ErrorCode == "invalid-name", result.ErrorCode ?? "<null>");
            ChecksContext.Equal("文件内容未变", original, fs.ReadAllText(outer));
            ChecksContext.Equal("没有写入记录", 0, result.WrittenPaths.Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckMissingDescriptor(IFileSystem fs)
    {
        Console.WriteLine("[ModDescriptorEditorChecks] 没有描述符文件时拒绝");
        var noPath = new ModDescriptorInfo(
            ModDescriptorFamily.Metadata,
            null,
            "v3_mod",
            "v3_mod",
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            null,
            null);
        var result = ModDescriptorEditor.Save(noPath, new ModDescriptorEdit("N", "1", string.Empty, [], null), fs);
        ChecksContext.Check("Metadata + 无路径 → no-descriptor", !result.Ok && result.ErrorCode == "no-descriptor", result.ErrorCode ?? "<null>");

        var none = noPath with { Family = ModDescriptorFamily.None };
        var noneResult = ModDescriptorEditor.Save(none, new ModDescriptorEdit("N", "1", string.Empty, [], null), fs);
        ChecksContext.Check(
            "Family None → unsupported-descriptor",
            !noneResult.Ok && noneResult.ErrorCode == "unsupported-descriptor",
            noneResult.ErrorCode ?? "<null>");
    }

    private static void CheckLegacyRollback(IFileSystem fs)
    {
        Console.WriteLine("[ModDescriptorEditorChecks] 第二个文件写失败时回滚第一个");
        var root = PlaysetChecks.CreateTempRoot(fs, "descriptor-rollback");
        try
        {
            var modRoot = Path.Combine(root, "mod");
            var target = Path.Combine(modRoot, "ck3_mod");
            fs.EnsureDirectory(target);
            var outer = Path.Combine(modRoot, "ck3_mod.mod");
            var inner = Path.Combine(target, "descriptor.mod");
            const string innerOriginal = "name=\"Old\"\nversion=\"0.1\"\n";
            const string outerOriginal = "name=\"Old\"\nversion=\"0.1\"\npath=\"C:/mods/ck3_mod\"\n";
            fs.WriteAllTextAtomic(inner, innerOriginal);
            fs.WriteAllTextAtomic(outer, outerOriginal);

            // 只读外层让第二次写入失败（内层先写、外层后写）。
            File.SetAttributes(outer, FileAttributes.ReadOnly);
            try
            {
                var result = ModDescriptorEditor.Save(
                    LegacyInfo(outer, "ck3_mod", "ck3_mod", "Old", "0.1", string.Empty, [], null),
                    new ModDescriptorEdit("New", "2.0", "1.9.*", ["X"], null),
                    fs);

                ChecksContext.Check(
                    "返回 io: 错误",
                    !result.Ok && result.ErrorCode?.StartsWith("io:", StringComparison.Ordinal) == true,
                    result.ErrorCode ?? "<null>");
                ChecksContext.Equal("内层已回滚为原文", innerOriginal, fs.ReadAllText(inner));
                ChecksContext.Equal("外层保持原文", outerOriginal, fs.ReadAllText(outer));
            }
            finally
            {
                File.SetAttributes(outer, FileAttributes.Normal);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckLegacyQuotes(IFileSystem fs)
    {
        Console.WriteLine("[ModDescriptorEditorChecks] 引号与反斜杠转义");
        var root = PlaysetChecks.CreateTempRoot(fs, "descriptor-quotes");
        try
        {
            var modRoot = Path.Combine(root, "mod");
            var target = Path.Combine(modRoot, "ck3_mod");
            fs.EnsureDirectory(target);
            var outer = Path.Combine(modRoot, "ck3_mod.mod");
            fs.WriteAllTextAtomic(outer, "name=\"Old\"\nversion=\"0.1\"\n");

            var result = ModDescriptorEditor.Save(
                LegacyInfo(outer, "ck3_mod", "ck3_mod", "Old", "0.1", string.Empty, [], null),
                new ModDescriptorEdit("中文 \"引号\" \\ 反斜杠", "2.0", string.Empty, [], null),
                fs);

            ChecksContext.Check("保存成功", result.Ok, result.ErrorCode ?? string.Empty);
            var text = fs.ReadAllText(outer);
            const string expected = "name=\"中文 \\\"引号\\\" \\\\ 反斜杠\"";
            ChecksContext.Check("引号与反斜杠已转义", text.Contains(expected, StringComparison.Ordinal), text.Split('\n')[0]);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    private static ModDescriptorInfo MetadataInfo(
        string path,
        string folder,
        string name,
        string version,
        string supported,
        IReadOnlyList<string> tags,
        string? description) =>
        new(ModDescriptorFamily.Metadata, path, folder, name, version, supported, tags, description, null);

    private static ModDescriptorInfo LegacyInfo(
        string path,
        string folder,
        string name,
        string displayName,
        string version,
        string supported,
        IReadOnlyList<string> tags,
        string? description) =>
        new(ModDescriptorFamily.Legacy, path, folder, displayName, version, supported, tags, description, null);
}
