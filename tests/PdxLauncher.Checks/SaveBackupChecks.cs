namespace PdxLauncher.Checks;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Saves;

/// <summary>
/// 自检：存档备份/恢复/安全删除服务。全部在系统临时目录里造数据，绝不读取或写入真机存档。
/// <para>覆盖命名规则（CJK/emoji/非法字符/120 字节截断/冲突后缀/保留名）、sidecar 字段、
/// sidecar 原子写（成功计数 + 半截失败清理）、备份与恢复、overwrite 回收站、哈希/大小守卫、
/// 删除备份与真实存档、配额、占用、取消、清理与隔离。</para>
/// </summary>
public static class SaveBackupChecks
{
    private const string Ck3 = "ck3";
    private const string SourceName = "神罗 存档 🐟.ck3";
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 5, 8, 30, 15, TimeSpan.Zero);
    private static readonly string[] IllegalCharacters = ["<", ">", ":", "\"", "/", "\\", "|", "?", "*"];

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var root = PlaysetChecks.CreateTempRoot(fs, "saves-backup");
        try
        {
            CheckConstants();
            CheckNaming();
            CheckPathRules(root);
            CheckBackupAndSidecar(fs, logger, root);
            CheckRestore(fs, logger, root);
            CheckHashGuards(fs, logger, root);
            CheckDeleteBackup(fs, logger, root);
            CheckDeleteBackupLocked(fs, logger, root);
            CheckOrphanListing(fs, logger, root);
            CheckMoveFailure(fs, logger, root);
            CheckSidecarAtomicWrite(fs, logger, root);
            CheckSidecarWriteFailure(fs, logger, root);
            CheckDeleteSave(fs, logger, root);
            CheckQuota(fs, logger, root);
            CheckLockedFile(fs, logger, root);
            CheckCancellation(fs, logger, root);
            CheckPrune(fs, logger, root);
            CheckUsage(fs, logger, root);
            CheckIsolation(fs, logger, root);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 常量与命名
    // ------------------------------------------------------------------

    private static void CheckConstants()
    {
        Console.WriteLine("[SaveBackupChecks] 常量与状态");
        ChecksContext.Equal("默认总量配额 2 GiB", 2L * 1024 * 1024 * 1024, SaveBackupPaths.DefaultMaxTotalBytes);
        ChecksContext.Equal("默认条目配额 200", 200, SaveBackupPaths.DefaultMaxEntriesPerGame);
        ChecksContext.Equal("安全名字节上限 120", 120, SaveBackupPaths.MaxSafeNameBytes);
        ChecksContext.Equal("备份根目录名", "save-backups", SaveBackupPaths.RootFolderName);
        ChecksContext.Equal("回收站目录名", "trash", SaveBackupPaths.TrashFolderName);
        ChecksContext.Equal("备份正文扩展名", ".bak", SaveBackupPaths.BackupExtension);
        ChecksContext.Equal("sidecar 扩展名", ".json", SaveBackupPaths.SidecarExtension);
        ChecksContext.Equal("恢复结果状态数", 6, (int)SaveRestoreStatus.Failed);
        ChecksContext.Equal("删除结果状态数", 3, (int)SaveDeleteStatus.Failed);
    }

    private static void CheckNaming()
    {
        Console.WriteLine("[SaveBackupChecks] 命名规则");
        ChecksContext.Equal("中文与 emoji 原样保留", SourceName, SaveBackupPaths.SafeFileName(SourceName));
        ChecksContext.Equal("日文与重音字符原样保留", "Königreich 日本.ck3", SaveBackupPaths.SafeFileName("Königreich 日本.ck3"));

        var illegal = string.Concat(IllegalCharacters) + ".ck3";
        ChecksContext.Equal(
            "非法字符全部替换为下划线",
            new string('_', IllegalCharacters.Length) + ".ck3",
            SaveBackupPaths.SafeFileName(illegal));

        ChecksContext.Equal("控制字符替换为下划线", "a_b.ck3", SaveBackupPaths.SafeFileName("a\tb.ck3"));
        ChecksContext.Equal("去掉尾随点与空格", "save.ck3", SaveBackupPaths.SafeFileName("save.ck3. . "));
        ChecksContext.Equal("空文件名回退 save", "save", SaveBackupPaths.SafeFileName("   "));
        ChecksContext.Equal("null 文件名回退 save", "save", SaveBackupPaths.SafeFileName(null!));
        ChecksContext.Equal("DOS 保留名加前缀", "_CON", SaveBackupPaths.SafeFileName("CON"));
        ChecksContext.Equal("带扩展名的保留名加前缀", "_con.ck3", SaveBackupPaths.SafeFileName("con.ck3"));
        ChecksContext.Equal("LPT9 也是保留名", "_LPT9.ck3", SaveBackupPaths.SafeFileName("LPT9.ck3"));

        var longCjk = SaveBackupPaths.SafeFileName(new string('长', 70) + ".ck3");
        ChecksContext.Equal("超长中文名截到 120 字节", 120, Encoding.Unicode.GetByteCount(longCjk));
        ChecksContext.Check("超长中文名保留扩展名", longCjk.EndsWith(".ck3", StringComparison.Ordinal), longCjk);
        ChecksContext.Equal("超长中文名主体 56 字", new string('长', 56) + ".ck3", longCjk);

        var emojiName = string.Concat(Enumerable.Repeat("🐟", 30)) + ".ck3";
        var trimmedEmoji = SaveBackupPaths.SafeFileName(emojiName);
        ChecksContext.Equal("超长 emoji 名截到 120 字节", 120, Encoding.Unicode.GetByteCount(trimmedEmoji));
        ChecksContext.Equal("emoji 截断不劈开代理对", string.Concat(Enumerable.Repeat("🐟", 28)) + ".ck3", trimmedEmoji);
        ChecksContext.Check("截断结果代理对完整", HasWellFormedSurrogates(trimmedEmoji));

        ChecksContext.Equal("冲突后缀 -2", "a-2.ck3", SaveBackupPaths.WithConflictSuffix("a.ck3", 2));
        ChecksContext.Equal("冲突后缀 -10", "a-10.ck3", SaveBackupPaths.WithConflictSuffix("a.ck3", 10));
        var longConflict = SaveBackupPaths.WithConflictSuffix(new string('长', 60) + ".v3", 2);
        ChecksContext.Equal("冲突后缀仍限 120 字节", 120, Encoding.Unicode.GetByteCount(longConflict));
        ChecksContext.Check("冲突后缀保留扩展名", longConflict.EndsWith("-2.v3", StringComparison.Ordinal), longConflict);

        ChecksContext.Equal("gameId 目录名非法字符替换", "a_b", SaveBackupPaths.SafeGameFolderName("a/b"));
        ChecksContext.Check(
            "gameId 目录名限 64 字节",
            Encoding.Unicode.GetByteCount(SaveBackupPaths.SafeGameFolderName(new string('字', 80))) <= 64);
    }

    private static void CheckPathRules(string root)
    {
        Console.WriteLine("[SaveBackupChecks] 路径规则");
        var local = BaseTime.ToLocalTime();
        ChecksContext.Equal("备份根目录", Path.Combine(root, "save-backups"), SaveBackupPaths.Root(root));
        ChecksContext.Equal(
            "游戏备份目录",
            Path.Combine(root, "save-backups", Ck3),
            SaveBackupPaths.GameDirectory(root, Ck3));
        ChecksContext.Equal(
            "回收站目录",
            Path.Combine(root, "save-backups", "trash", Ck3),
            SaveBackupPaths.TrashDirectory(root, Ck3));
        ChecksContext.Equal("sidecar 路径", "x.bak.json", SaveBackupPaths.SidecarPath("x.bak"));
        ChecksContext.Equal(
            "备份时间戳格式",
            SaveBackupPaths.BackupStamp(local) + "__a.ck3.bak",
            SaveBackupPaths.BackupFileName(local, "a.ck3"));
        var restoreName = SaveBackupPaths.RestoreFileName("a.ck3", local);
        ChecksContext.Check("恢复名含 (恢复 yyyyMMdd-HHmm)", restoreName.Contains(" (恢复 ", StringComparison.Ordinal), restoreName);
        ChecksContext.Check("恢复名保留扩展名", restoreName.EndsWith(".ck3", StringComparison.Ordinal), restoreName);
        ChecksContext.Check("恢复名不超 240 字节", Encoding.Unicode.GetByteCount(restoreName) <= 240);
        var longRestore = SaveBackupPaths.RestoreFileName(new string('长', 200) + ".ck3", local);
        ChecksContext.Check("超长原名恢复仍保留扩展名", longRestore.EndsWith(".ck3", StringComparison.Ordinal), longRestore);
        ChecksContext.Check(
            "超长原名恢复不超 240 字节",
            Encoding.Unicode.GetByteCount(longRestore) <= 240,
            Encoding.Unicode.GetByteCount(longRestore).ToString(CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------------
    // 备份与 sidecar
    // ------------------------------------------------------------------

    private static void CheckBackupAndSidecar(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 备份与 sidecar");
        var fixture = CreateFixture(fs, logger, root, "backup");
        var sourceBytes = Encoding.UTF8.GetBytes("hello 存档 🐟 payload");
        var sourcePath = WriteSource(fixture, SourceName, sourceBytes);
        var expectedHash = Sha256Hex(sourceBytes);
        var meta = CreateMeta(sourcePath, SourceName, sourceBytes.Length);

        var result = Await(fixture.Service.BackupAsync(Ck3, sourcePath, meta, CancellationToken.None));
        ChecksContext.Equal("备份成功", SaveBackupStatus.Ok, result.Status);
        ChecksContext.Check("备份结果带条目", result.Entry is not null, result.Message);
        if (result.Entry is null)
        {
            return;
        }

        var entry = result.Entry;
        var backupDirectory = SaveBackupPaths.GameDirectory(fixture.DataRoot, Ck3);
        ChecksContext.Equal(
            "备份落在 <data>/save-backups/<gameId>",
            backupDirectory,
            Path.GetDirectoryName(entry.BackupPath));
        ChecksContext.Equal(
            "备份文件名 = 时间戳__安全名.bak",
            Path.Combine(backupDirectory, SaveBackupPaths.BackupFileName(BaseTime.ToLocalTime(), SourceName)),
            entry.BackupPath);
        ChecksContext.Equal("备份正文存在", true, fs.FileExists(entry.BackupPath));
        ChecksContext.EqualBytes("备份正文与源一致", sourceBytes, fs.ReadAllBytes(entry.BackupPath));
        ChecksContext.Equal("条目字节数", (long)sourceBytes.Length, entry.SizeBytes);
        ChecksContext.Equal("条目 SHA-256", expectedHash, entry.Sha256);
        ChecksContext.Equal("条目 gameId", Ck3, entry.GameId);
        ChecksContext.Equal("条目游戏版本", "1.13.0", entry.GameVersion);
        ChecksContext.Equal("条目游戏日期", "1066.9.15", entry.GameDate);
        ChecksContext.Equal("条目创建时间", BaseTime, entry.CreatedUtc);
        ChecksContext.Equal("条目原始路径", sourcePath, entry.OriginalPath);
        ChecksContext.Equal("条目原始文件名", SourceName, entry.OriginalFileName);
        ChecksContext.Equal("sidecar 路径", entry.BackupPath + ".json", entry.SidecarPath);
        ChecksContext.Equal("sidecar 存在", true, fs.FileExists(entry.SidecarPath));
        ChecksContext.Equal("源存档只读未被移动", true, fs.FileExists(sourcePath));
        ChecksContext.EqualBytes("源存档内容未变", sourceBytes, fs.ReadAllBytes(sourcePath));
        ChecksContext.Equal("没有遗留 .tmp", 0, CountFiles(fs, backupDirectory, "*.tmp"));
        ChecksContext.Equal("备份目录只有正文与 sidecar", 2, CountFiles(fs, backupDirectory, "*"));

        using (var document = JsonDocument.Parse(fs.ReadAllText(entry.SidecarPath)))
        {
            var sidecar = document.RootElement;
            ChecksContext.Equal("sidecar schemaVersion=1", 1, sidecar.GetProperty("schemaVersion").GetInt32());
            ChecksContext.Equal("sidecar originalPath", sourcePath, sidecar.GetProperty("originalPath").GetString());
            ChecksContext.Equal("sidecar originalFileName", SourceName, sidecar.GetProperty("originalFileName").GetString());
            ChecksContext.Equal("sidecar sizeBytes", (long)sourceBytes.Length, sidecar.GetProperty("sizeBytes").GetInt64());
            ChecksContext.Equal("sidecar sha256", expectedHash, sidecar.GetProperty("sha256").GetString());
            ChecksContext.Equal("sidecar gameId", Ck3, sidecar.GetProperty("gameId").GetString());
            ChecksContext.Equal("sidecar gameVersion", "1.13.0", sidecar.GetProperty("gameVersion").GetString());
            ChecksContext.Equal("sidecar gameDate", "1066.9.15", sidecar.GetProperty("gameDate").GetString());
            ChecksContext.Equal("sidecar saveName", "神罗", sidecar.GetProperty("saveName").GetString());
            ChecksContext.Equal("sidecar createdUtc", BaseTime, sidecar.GetProperty("createdUtc").GetDateTimeOffset());
            ChecksContext.Check(
                "sidecar 不含 playerName 等无关隐私",
                !sidecar.TryGetProperty("playerName", out _) && !sidecar.TryGetProperty("ironman", out _));
        }

        var aliasBytes = Encoding.UTF8.GetBytes("cs2 alias");
        var aliasPath = WriteSource(fixture, "alias.ck3", aliasBytes);
        var alias = Await(fixture.Service.BackupAsync("cities_v2", aliasPath, null, CancellationToken.None));
        ChecksContext.Equal("别名归一后 gameId 为 cities_skylines", "cities_skylines", alias.Entry?.GameId);
        ChecksContext.Equal(
            "别名归一的目录名",
            SaveBackupPaths.GameDirectory(fixture.DataRoot, "cities_skylines"),
            alias.Entry is null ? string.Empty : Path.GetDirectoryName(alias.Entry.BackupPath));
        ChecksContext.Equal("按别名查询也能列出备份", 1, Await(fixture.Service.ListBackupsAsync("cities_v2", CancellationToken.None)).Count);

        var emptyPath = WriteSource(fixture, "empty.ck3", Array.Empty<byte>());
        var empty = Await(fixture.Service.BackupAsync("stellaris", emptyPath, null, CancellationToken.None));
        ChecksContext.Equal("0 字节文件也能备份", SaveBackupStatus.Ok, empty.Status);
        ChecksContext.Equal("0 字节条目大小", 0L, empty.Entry?.SizeBytes ?? -1);
        ChecksContext.Equal(
            "0 字节 sha256 为空串哈希",
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            empty.Entry?.Sha256);

        var listed = Await(fixture.Service.ListBackupsAsync(Ck3, CancellationToken.None));
        ChecksContext.Equal("ListBackups 只返回本游戏条目", 1, listed.Count);
        ChecksContext.Equal("ListBackups 返回刚备份的条目", entry.BackupPath, listed.Count == 0 ? string.Empty : listed[0].BackupPath);

        var missing = Await(fixture.Service.BackupAsync(Ck3, Path.Combine(fixture.SavesDirectory, "nope.ck3"), null, CancellationToken.None));
        ChecksContext.Equal("源不存在返回 SourceMissing", SaveBackupStatus.SourceMissing, missing.Status);
        var invalidGame = Await(fixture.Service.BackupAsync("", sourcePath, null, CancellationToken.None));
        ChecksContext.Equal("空 gameId 返回 InvalidArgument", SaveBackupStatus.InvalidArgument, invalidGame.Status);
    }

    // ------------------------------------------------------------------
    // 恢复
    // ------------------------------------------------------------------

    private static void CheckRestore(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 恢复（默认不覆盖 / overwrite 进回收站）");
        var fixture = CreateFixture(fs, logger, root, "restore");
        var originalBytes = Encoding.UTF8.GetBytes("原始存档内容 v1");
        var sourcePath = WriteSource(fixture, SourceName, originalBytes);
        var backup = Await(fixture.Service.BackupAsync(Ck3, sourcePath, null, CancellationToken.None));
        ChecksContext.Check("恢复用例的前置备份成功", backup.Entry is not null, backup.Message);
        if (backup.Entry is null)
        {
            return;
        }

        var entry = backup.Entry;
        var local = BaseTime.ToLocalTime();
        var restoreName = SaveBackupPaths.RestoreFileName(SourceName, local);

        var first = Await(fixture.Service.RestoreAsync(entry, overwrite: false, CancellationToken.None));
        ChecksContext.Equal("默认恢复成功", SaveRestoreStatus.Ok, first.Status);
        ChecksContext.Equal("默认恢复写同目录 (恢复 …) 名", Path.Combine(fixture.SavesDirectory, restoreName), first.RestoredPath);
        ChecksContext.Equal("默认恢复不覆盖原档", true, fs.FileExists(sourcePath));
        ChecksContext.Equal("默认恢复无回收站动作", null, first.TrashPath);
        if (first.RestoredPath is not null)
        {
            ChecksContext.EqualBytes("恢复出的内容与备份一致", originalBytes, fs.ReadAllBytes(first.RestoredPath));
        }

        var second = Await(fixture.Service.RestoreAsync(entry, overwrite: false, CancellationToken.None));
        ChecksContext.Equal("同秒二次恢复成功", SaveRestoreStatus.Ok, second.Status);
        ChecksContext.Check(
            "同秒二次恢复追加 -2",
            second.RestoredPath is not null && second.RestoredPath.EndsWith("-2.ck3", StringComparison.Ordinal),
            second.RestoredPath ?? "(null)");
        ChecksContext.Check(
            "二次恢复目标确实是一个新文件",
            second.RestoredPath is not null &&
            first.RestoredPath is not null &&
            !string.Equals(first.RestoredPath, second.RestoredPath, StringComparison.Ordinal));

        var newerBytes = Encoding.UTF8.GetBytes("较新的存档内容 v2");
        fs.WriteAllBytesAtomic(sourcePath, newerBytes);
        var overwrite = Await(fixture.Service.RestoreAsync(entry, overwrite: true, CancellationToken.None));
        ChecksContext.Equal("overwrite 恢复成功", SaveRestoreStatus.Ok, overwrite.Status);
        ChecksContext.Equal("overwrite 写回原名", sourcePath, overwrite.RestoredPath);
        ChecksContext.Check("overwrite 记录被替换存档进了回收站", overwrite.TrashPath is not null, overwrite.Message);
        if (overwrite.TrashPath is not null)
        {
            var trashDirectory = SaveBackupPaths.TrashDirectory(fixture.DataRoot, Ck3);
            ChecksContext.Equal(
                "回收站路径在 <data>/save-backups/trash/<gameId> 下",
                Path.Combine(trashDirectory, SaveBackupPaths.TrashFileName(local, SourceName)),
                overwrite.TrashPath);
            ChecksContext.Check(
                "回收站路径前缀正确",
                overwrite.TrashPath.StartsWith(trashDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                overwrite.TrashPath);
            ChecksContext.EqualBytes("回收站保存的是被替换的旧内容", newerBytes, fs.ReadAllBytes(overwrite.TrashPath));
        }

        ChecksContext.EqualBytes("overwrite 后原名文件是备份内容", originalBytes, fs.ReadAllBytes(sourcePath));
        ChecksContext.Equal("覆盖恢复后没有 .restore.part 残留", 0, CountFiles(fs, fixture.SavesDirectory, "*.restore.part"));
    }

    // ------------------------------------------------------------------
    // 哈希 / 大小守卫
    // ------------------------------------------------------------------

    private static void CheckHashGuards(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 校验守卫");
        var fixture = CreateFixture(fs, logger, root, "guards");
        var bytes = Encoding.UTF8.GetBytes("guard payload 存档");
        var sourcePath = WriteSource(fixture, SourceName, bytes);

        var corrupted = Await(fixture.Service.BackupAsync(Ck3, sourcePath, null, CancellationToken.None)).Entry;
        ChecksContext.Check("哈希守卫用例备份成功", corrupted is not null);
        if (corrupted is null)
        {
            return;
        }

        var corruptedBytes = fs.ReadAllBytes(corrupted.BackupPath);
        corruptedBytes[0] ^= 0xFF;
        fs.WriteAllBytesAtomic(corrupted.BackupPath, corruptedBytes);
        var beforeCount = CountFiles(fs, fixture.SavesDirectory, "*");
        var hashMismatch = Await(fixture.Service.RestoreAsync(corrupted, overwrite: false, CancellationToken.None));
        ChecksContext.Equal("哈希不一致返回 HashMismatch", SaveRestoreStatus.HashMismatch, hashMismatch.Status);
        ChecksContext.Equal("哈希不一致不写任何文件", beforeCount, CountFiles(fs, fixture.SavesDirectory, "*"));
        ChecksContext.Equal("哈希不一致没有 .restore.part", 0, CountFiles(fs, fixture.SavesDirectory, "*.restore.part"));

        var sized = Await(fixture.Service.BackupAsync(Ck3, WriteSource(fixture, "sized.ck3", bytes), null, CancellationToken.None)).Entry;
        ChecksContext.Check("大小守卫用例备份成功", sized is not null);
        if (sized is not null)
        {
            var longer = fs.ReadAllBytes(sized.BackupPath).ToList();
            longer.Add(0x00);
            fs.WriteAllBytesAtomic(sized.BackupPath, longer.ToArray());
            var beforeSizeCount = CountFiles(fs, fixture.SavesDirectory, "*");
            var sizeMismatch = Await(fixture.Service.RestoreAsync(sized, overwrite: false, CancellationToken.None));
            ChecksContext.Equal("大小不一致返回 SizeMismatch", SaveRestoreStatus.SizeMismatch, sizeMismatch.Status);
            ChecksContext.Equal("大小不一致不写任何文件", beforeSizeCount, CountFiles(fs, fixture.SavesDirectory, "*"));
        }

        var staleSource = WriteSource(fixture, "stale.ck3", bytes);
        var stale = Await(fixture.Service.BackupAsync(Ck3, staleSource, null, CancellationToken.None)).Entry;
        ChecksContext.Check("过期条目用例备份成功", stale is not null);
        if (stale is not null)
        {
            var staleEntry = stale with { Sha256 = new string('0', 64) };
            var staleResult = Await(fixture.Service.RestoreAsync(staleEntry, overwrite: false, CancellationToken.None));
            ChecksContext.Equal("条目哈希与 sidecar 不符返回 InvalidArgument", SaveRestoreStatus.InvalidArgument, staleResult.Status);

            fs.Delete(stale.SidecarPath);
            var missingSidecar = Await(fixture.Service.RestoreAsync(stale, overwrite: false, CancellationToken.None));
            ChecksContext.Equal("sidecar 缺失返回 SidecarMissing", SaveRestoreStatus.SidecarMissing, missingSidecar.Status);

            var missingBackup = stale with { BackupPath = Path.Combine(SaveBackupPaths.GameDirectory(fixture.DataRoot, Ck3), "gone.bak") };
            var missingResult = Await(fixture.Service.RestoreAsync(missingBackup, overwrite: false, CancellationToken.None));
            ChecksContext.Equal("备份正文缺失返回 BackupMissing", SaveRestoreStatus.BackupMissing, missingResult.Status);
        }
    }

    // ------------------------------------------------------------------
    // 删除备份
    // ------------------------------------------------------------------

    private static void CheckDeleteBackup(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 删除备份");
        var fixture = CreateFixture(fs, logger, root, "delbak");
        var first = Await(fixture.Service.BackupAsync(Ck3, WriteSource(fixture, "d1.ck3", [1, 2, 3]), null, CancellationToken.None)).Entry;
        ChecksContext.Check("删除备份用例一备份成功", first is not null);
        if (first is null)
        {
            return;
        }

        var deleted = Await(fixture.Service.DeleteBackupAsync(first, CancellationToken.None));
        ChecksContext.Equal("删除备份成功", SaveDeleteStatus.Ok, deleted.Status);
        ChecksContext.Equal("备份正文已消失", false, fs.FileExists(first.BackupPath));
        ChecksContext.Equal("sidecar 已消失", false, fs.FileExists(first.SidecarPath));
        ChecksContext.Equal("删除备份后 SourceStillPresent=false", false, deleted.SourceStillPresent);
        ChecksContext.Equal("删除备份后残留清单为空", 0, deleted.RemainingPaths.Count);

        var withSidecar = Await(fixture.Service.BackupAsync(Ck3, WriteSource(fixture, "d2.ck3", [4, 5, 6]), null, CancellationToken.None)).Entry;
        ChecksContext.Check("删除备份用例二备份成功", withSidecar is not null);
        if (withSidecar is not null)
        {
            fs.Delete(withSidecar.SidecarPath);
            var sidecarGone = Await(fixture.Service.DeleteBackupAsync(withSidecar, CancellationToken.None));
            ChecksContext.Equal("sidecar 缺失仍能删 .bak", SaveDeleteStatus.Ok, sidecarGone.Status);
            ChecksContext.Equal("sidecar 缺失时 .bak 也已消失", false, fs.FileExists(withSidecar.BackupPath));

            var again = Await(fixture.Service.DeleteBackupAsync(withSidecar, CancellationToken.None));
            ChecksContext.Equal("重复删除返回 NotFound", SaveDeleteStatus.NotFound, again.Status);
        }

        var emptyEntry = new SaveBackupEntry(string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, Ck3, null, null, BaseTime);
        var invalid = Await(fixture.Service.DeleteBackupAsync(emptyEntry, CancellationToken.None));
        ChecksContext.Equal("空备份路径返回 InvalidArgument", SaveDeleteStatus.InvalidArgument, invalid.Status);
    }

    // ------------------------------------------------------------------
    // 删除真实存档（进回收站）
    // ------------------------------------------------------------------

    private static void CheckDeleteSave(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 安全删除真实存档");
        var fixture = CreateFixture(fs, logger, root, "delsave");
        var bytes = Encoding.UTF8.GetBytes("待删除存档 🗑");
        var sourcePath = WriteSource(fixture, "delete-me.ck3", bytes);

        var deleted = Await(fixture.Service.DeleteSaveAsync(Ck3, sourcePath, CancellationToken.None));
        ChecksContext.Equal("删除真实存档成功", SaveDeleteStatus.Ok, deleted.Status);
        ChecksContext.Equal("删除后源不在原处", false, fs.FileExists(sourcePath));
        ChecksContext.Equal("删除后 SourceStillPresent=false", false, deleted.SourceStillPresent);
        ChecksContext.Check("删除后回收站有文件", deleted.TrashedPath is not null && fs.FileExists(deleted.TrashedPath), deleted.Message);
        if (deleted.TrashedPath is not null)
        {
            ChecksContext.Equal(
                "回收站目录为 <data>/save-backups/trash/<gameId>",
                SaveBackupPaths.TrashDirectory(fixture.DataRoot, Ck3),
                Path.GetDirectoryName(deleted.TrashedPath));
            ChecksContext.EqualBytes("回收站内容与源一致", bytes, fs.ReadAllBytes(deleted.TrashedPath));
        }

        var again = Await(fixture.Service.DeleteSaveAsync(Ck3, sourcePath, CancellationToken.None));
        ChecksContext.Equal("重复删除返回 NotFound", SaveDeleteStatus.NotFound, again.Status);

        var existing = WriteSource(fixture, "empty-game.ck3", [9, 9]);
        var emptyGame = Await(fixture.Service.DeleteSaveAsync("", existing, CancellationToken.None));
        ChecksContext.Equal("空 gameId 返回 InvalidArgument", SaveDeleteStatus.InvalidArgument, emptyGame.Status);
        ChecksContext.Equal("空 gameId 时源仍在", true, fs.FileExists(existing));

        var lockedPath = WriteSource(fixture, "locked-delete.ck3", [7, 7, 7]);
        using (var handle = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var locked = Await(fixture.Service.DeleteSaveAsync(Ck3, lockedPath, CancellationToken.None));
            ChecksContext.Equal("占用时删除返回 Failed", SaveDeleteStatus.Failed, locked.Status);
            ChecksContext.Equal("占用时源仍在", true, fs.FileExists(lockedPath));
            ChecksContext.Equal("占用时 SourceStillPresent=true", true, locked.SourceStillPresent);
            ChecksContext.Check(
                "占用时消息带 Win32 共享冲突",
                locked.Message.Contains("0x80070020", StringComparison.OrdinalIgnoreCase),
                locked.Message);
        }
    }

    // ------------------------------------------------------------------
    // 配额
    // ------------------------------------------------------------------

    private static void CheckQuota(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 配额");
        var fixture = CreateFixture(fs, logger, root, "quota");
        var totalService = new SaveBackupService(fs, logger, fixture.DataRoot, fixture.Clock, maxTotalBytes: 1000);
        var size600 = new byte[600];
        var r1 = Await(totalService.BackupAsync(Ck3, WriteSource(fixture, "q1.ck3", size600), null, CancellationToken.None));
        ChecksContext.Equal("总量配额内第一次备份成功", SaveBackupStatus.Ok, r1.Status);
        var r2 = Await(totalService.BackupAsync(Ck3, WriteSource(fixture, "q2.ck3", size600), null, CancellationToken.None));
        ChecksContext.Equal("超出总量返回 QuotaExceeded", SaveBackupStatus.QuotaExceeded, r2.Status);
        ChecksContext.Equal("配额超限返回当前总字节", 600L, r2.Usage?.TotalBytes ?? -1);
        ChecksContext.Equal("配额超限返回当前条目数", 1, r2.Usage?.EntryCount ?? -1);
        ChecksContext.Check("配额消息说明总量", r2.Message.Contains("配额", StringComparison.Ordinal), r2.Message);

        var entriesFixture = CreateFixture(fs, logger, root, "quotaentries");
        var entriesService = new SaveBackupService(fs, logger, entriesFixture.DataRoot, entriesFixture.Clock, maxEntriesPerGame: 1);
        var e1 = Await(entriesService.BackupAsync(Ck3, WriteSource(entriesFixture, "e1.ck3", [1]), null, CancellationToken.None));
        ChecksContext.Equal("条目配额内第一次备份成功", SaveBackupStatus.Ok, e1.Status);
        var e2 = Await(entriesService.BackupAsync(Ck3, WriteSource(entriesFixture, "e2.ck3", [2]), null, CancellationToken.None));
        ChecksContext.Equal("超出条目数返回 QuotaExceeded", SaveBackupStatus.QuotaExceeded, e2.Status);
        ChecksContext.Equal("条目超限返回当前条目数", 1, e2.Usage?.EntryCount ?? -1);
        ChecksContext.Check("配额消息说明条目上限", e2.Message.Contains("条目", StringComparison.Ordinal), e2.Message);
        ChecksContext.Equal("配额超限不自动清理旧备份", 2, CountFiles(fs, SaveBackupPaths.GameDirectory(entriesFixture.DataRoot, Ck3), "*"));
    }

    // ------------------------------------------------------------------
    // 占用与取消
    // ------------------------------------------------------------------

    private static void CheckLockedFile(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 文件被占用");
        var fixture = CreateFixture(fs, logger, root, "locked");
        var sourcePath = WriteSource(fixture, "locked.ck3", Encoding.UTF8.GetBytes("locked payload"));
        using (var handle = new FileStream(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = Await(fixture.Service.BackupAsync(Ck3, sourcePath, null, CancellationToken.None));
            ChecksContext.Equal("占用时备份返回 Failed", SaveBackupStatus.Failed, result.Status);
            ChecksContext.Check(
                "占用时消息带 Win32 共享冲突码",
                result.Message.Contains("0x80070020", StringComparison.OrdinalIgnoreCase),
                result.Message);
            ChecksContext.Equal("占用时不抛异常且无 .bak", 0, CountFiles(fs, SaveBackupPaths.GameDirectory(fixture.DataRoot, Ck3), "*.bak"));
            ChecksContext.Equal("占用时不留下 .tmp", 0, CountFiles(fs, SaveBackupPaths.GameDirectory(fixture.DataRoot, Ck3), "*.tmp"));
        }
    }

    private static void CheckCancellation(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 取消");
        var fixture = CreateFixture(fs, logger, root, "cancel");
        var sourcePath = WriteSource(fixture, "cancel.ck3", Encoding.UTF8.GetBytes("cancel payload"));

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            ChecksContext.Check(
                "预取消令牌抛 OperationCanceledException",
                ThrowsCancellation(() => fixture.Service.BackupAsync(Ck3, sourcePath, null, cts.Token)));
            ChecksContext.Equal("预取消不产生备份", 0, CountFiles(fs, SaveBackupPaths.GameDirectory(fixture.DataRoot, Ck3), "*"));
        }

        var bigBytes = new byte[8 * 1024 * 1024];
        bigBytes[0] = 0x42;
        bigBytes[^1] = 0x24;
        var bigPath = WriteSource(fixture, "big.ck3", bigBytes);
        using (var cts = new CancellationTokenSource())
        {
            var task = fixture.Service.BackupAsync("big", bigPath, null, cts.Token);
            cts.Cancel();
            var cancelled = false;
            try
            {
                Await(task);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            ChecksContext.Check("复制途中取消抛 OperationCanceledException", cancelled);
            ChecksContext.Equal(
                "取消后没有 .tmp/.bak 残留",
                0,
                CountFiles(fs, SaveBackupPaths.GameDirectory(fixture.DataRoot, "big"), "*"));
        }
    }

    // ------------------------------------------------------------------
    // 显式清理
    // ------------------------------------------------------------------

    private static void CheckPrune(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 显式清理");
        var fixture = CreateFixture(fs, logger, root, "prune");
        var sourcePath = WriteSource(fixture, "prune.ck3", Encoding.UTF8.GetBytes("prune v1"));
        fixture.Clock.UtcNow = BaseTime;
        var b0 = Await(fixture.Service.BackupAsync("prunegame", sourcePath, null, CancellationToken.None)).Entry;
        fs.WriteAllBytesAtomic(sourcePath, Encoding.UTF8.GetBytes("prune v2"));
        fixture.Clock.UtcNow = BaseTime.AddMinutes(1);
        var b1 = Await(fixture.Service.BackupAsync("prunegame", sourcePath, null, CancellationToken.None)).Entry;
        fs.WriteAllBytesAtomic(sourcePath, Encoding.UTF8.GetBytes("prune v3"));
        fixture.Clock.UtcNow = BaseTime.AddMinutes(2);
        var b2 = Await(fixture.Service.BackupAsync("prunegame", sourcePath, null, CancellationToken.None)).Entry;
        ChecksContext.Check("清理用例三次备份都成功", b0 is not null && b1 is not null && b2 is not null);
        if (b0 is null || b1 is null || b2 is null)
        {
            return;
        }

        var all = Await(fixture.Service.ListBackupsAsync("prunegame", CancellationToken.None));
        ChecksContext.Equal("清理前共有 3 个备份", 3, all.Count);
        ChecksContext.Equal("列表按时间降序，最新在前", b2.Sha256, all[0].Sha256);

        var prune = Await(fixture.Service.PruneAsync("prunegame", keepNewest: 1, CancellationToken.None));
        ChecksContext.Equal("保留最新清理成功", SaveBackupStatus.Ok, prune.Status);
        ChecksContext.Equal("清理删除 2 个", 2, prune.DeletedEntries);
        ChecksContext.Equal("清理失败 0 个", 0, prune.FailedDeletions);
        ChecksContext.Equal("清理后用量条目数为 1", 1, prune.Usage.EntryCount);
        var remaining = Await(fixture.Service.ListBackupsAsync("prunegame", CancellationToken.None));
        ChecksContext.Equal("清理后只剩 1 个备份", 1, remaining.Count);
        ChecksContext.Equal("保留的是最新的那个", b2.Sha256, remaining.Count == 0 ? string.Empty : remaining[0].Sha256);
        ChecksContext.Equal("较早备份正文已删除", false, fs.FileExists(b0.BackupPath));

        var negative = Await(fixture.Service.PruneAsync("prunegame", keepNewest: -1, CancellationToken.None));
        ChecksContext.Equal("keepNewest 为负返回 InvalidArgument", SaveBackupStatus.InvalidArgument, negative.Status);

        var zeroFixture = CreateFixture(fs, logger, root, "prunezero");
        var zeroSource = WriteSource(zeroFixture, "zero.ck3", [1]);
        zeroFixture.Clock.UtcNow = BaseTime;
        Await(zeroFixture.Service.BackupAsync("prunegame", zeroSource, null, CancellationToken.None));
        zeroFixture.Clock.UtcNow = BaseTime.AddMinutes(5);
        Await(zeroFixture.Service.BackupAsync("prunegame", zeroSource, null, CancellationToken.None));
        var zero = Await(zeroFixture.Service.PruneAsync("prunegame", keepNewest: 0, CancellationToken.None));
        ChecksContext.Equal("keepNewest=0 清空成功", SaveBackupStatus.Ok, zero.Status);
        ChecksContext.Equal("keepNewest=0 删除 2 个", 2, zero.DeletedEntries);
        ChecksContext.Equal("keepNewest=0 后没有备份", 0, Await(zeroFixture.Service.ListBackupsAsync("prunegame", CancellationToken.None)).Count);
        ChecksContext.Equal("keepNewest=0 后用量为 0", 0, zero.Usage.EntryCount);
    }

    // ------------------------------------------------------------------
    // 用量与隔离
    // ------------------------------------------------------------------

    private static void CheckUsage(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 用量统计");
        var fixture = CreateFixture(fs, logger, root, "usage");
        Await(fixture.Service.BackupAsync("usage", WriteSource(fixture, "u1.ck3", new byte[5]), null, CancellationToken.None));
        Await(fixture.Service.BackupAsync("usage", WriteSource(fixture, "u2.ck3", new byte[7]), null, CancellationToken.None));
        var usage = Await(fixture.Service.GetUsageAsync(CancellationToken.None));
        ChecksContext.Equal("用量总字节 = 5 + 7", 12L, usage.TotalBytes);
        ChecksContext.Equal("用量条目数 = 2", 2, usage.EntryCount);

        var emptyUsageFixture = CreateFixture(fs, logger, root, "usageempty");
        var emptyUsage = Await(emptyUsageFixture.Service.GetUsageAsync(CancellationToken.None));
        ChecksContext.Equal("没有备份时用量为 0 字节", 0L, emptyUsage.TotalBytes);
        ChecksContext.Equal("没有备份时条目数为 0", 0, emptyUsage.EntryCount);
    }

    private static void CheckIsolation(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 写入隔离");
        var fixture = CreateFixture(fs, logger, root, "isolation");
        var bytes = Encoding.UTF8.GetBytes("isolation payload");
        var sourcePath = WriteSource(fixture, SourceName, bytes);
        var entry = Await(fixture.Service.BackupAsync(Ck3, sourcePath, null, CancellationToken.None)).Entry;
        ChecksContext.Check("隔离用例备份成功", entry is not null);
        if (entry is null)
        {
            return;
        }

        var restored = Await(fixture.Service.RestoreAsync(entry, overwrite: true, CancellationToken.None));
        ChecksContext.Equal("隔离用例 overwrite 恢复成功", SaveRestoreStatus.Ok, restored.Status);
        ChecksContext.Check("隔离用例产生了回收站文件", restored.TrashPath is not null && fs.FileExists(restored.TrashPath));

        var backupRoot = SaveBackupPaths.Root(fixture.DataRoot);
        var outside = 0;
        var total = 0;
        foreach (var path in fs.EnumerateFiles(fixture.FixtureRoot, "*", recursive: true))
        {
            total++;
            var insideData = path.StartsWith(backupRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                             string.Equals(Path.GetDirectoryName(path), backupRoot, StringComparison.Ordinal);
            var insideSaves = path.StartsWith(fixture.SavesDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (!insideData && !insideSaves)
            {
                outside++;
            }
        }

        ChecksContext.Equal("所有写入都在 data 根或恢复目标目录内", 0, outside);
        ChecksContext.Check("隔离用例确实有产物", total > 0, total.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check("回收站确实在 data 根内", restored.TrashPath is not null && restored.TrashPath.StartsWith(backupRoot, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // 删除备份的顺序与降级条目
    // ------------------------------------------------------------------

    private static void CheckDeleteBackupLocked(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 删除备份正文被占用");
        var fixture = CreateFixture(fs, logger, root, "delbak-locked");
        var entry = Await(fixture.Service.BackupAsync(Ck3, WriteSource(fixture, "locked-backup.ck3", [1, 3, 5, 7]), null, CancellationToken.None)).Entry;
        ChecksContext.Check("占用删除用例备份成功", entry is not null);
        if (entry is null)
        {
            return;
        }

        using (var handle = new FileStream(entry.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var locked = Await(fixture.Service.DeleteBackupAsync(entry, CancellationToken.None));
            ChecksContext.Equal("正文被占用时删除返回 Failed", SaveDeleteStatus.Failed, locked.Status);
            ChecksContext.Equal("失败时 SourceStillPresent=true", true, locked.SourceStillPresent);
            ChecksContext.Check(
                "失败消息包含残留文件路径",
                locked.Message.Contains(entry.BackupPath, StringComparison.Ordinal),
                locked.Message);
            ChecksContext.Equal("正文删不掉时 sidecar 不能被先删", true, fs.FileExists(entry.SidecarPath));
        }

        var listed = Await(fixture.Service.ListBackupsAsync(Ck3, CancellationToken.None));
        ChecksContext.Check(
            "删除失败后列表仍能看到该备份",
            listed.Any(item => string.Equals(item.BackupPath, entry.BackupPath, StringComparison.Ordinal)),
            listed.Count.ToString(CultureInfo.InvariantCulture));

        var retried = Await(fixture.Service.DeleteBackupAsync(entry, CancellationToken.None));
        ChecksContext.Equal("释放占用后删除成功", SaveDeleteStatus.Ok, retried.Status);
        ChecksContext.Equal("释放后正文消失", false, fs.FileExists(entry.BackupPath));
        ChecksContext.Equal("释放后 sidecar 消失", false, fs.FileExists(entry.SidecarPath));
    }

    private static void CheckOrphanListing(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 缺 sidecar 的孤儿备份");
        var fixture = CreateFixture(fs, logger, root, "orphan");
        var payload = new byte[] { 2, 4, 6, 8 };
        var entry = Await(fixture.Service.BackupAsync(Ck3, WriteSource(fixture, "orphan.ck3", payload), null, CancellationToken.None)).Entry;
        ChecksContext.Check("孤儿用例备份成功", entry is not null);
        if (entry is null)
        {
            return;
        }

        fs.Delete(entry.SidecarPath);
        var listed = Await(fixture.Service.ListBackupsAsync(Ck3, CancellationToken.None));
        var usage = Await(fixture.Service.GetUsageAsync(CancellationToken.None));
        var orphan = listed.Count == 1 ? listed[0] : null;
        ChecksContext.Equal("缺 sidecar 仍列出 1 条", 1, listed.Count);
        ChecksContext.Equal("孤儿条目 sha 为空", string.Empty, orphan?.Sha256);
        ChecksContext.Equal("孤儿条目大小取自文件本身", (long)payload.Length, orphan?.SizeBytes);
        ChecksContext.Equal("用量条目数与列表一致", usage.EntryCount, listed.Count);
        ChecksContext.Equal("用量字节数与列表一致", usage.TotalBytes, listed.Sum(item => item.SizeBytes));
    }

    private static void CheckMoveFailure(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] 备份落盘 Move 失败");
        var fixture = CreateFixture(fs, logger, root, "movefail");
        var failing = new FailingMoveFileSystem(fs);
        var service = new SaveBackupService(failing, logger, fixture.DataRoot, fixture.Clock);
        var sourcePath = WriteSource(fixture, "move-fail.ck3", [11, 12, 13, 14, 15]);
        var result = Await(service.BackupAsync(Ck3, sourcePath, null, CancellationToken.None));
        var gameDirectory = SaveBackupPaths.GameDirectory(fixture.DataRoot, Ck3);
        ChecksContext.Equal("Move 抛异常时返回 Failed", SaveBackupStatus.Failed, result.Status);
        ChecksContext.Equal("失败后没有留下 .bak", 0, CountFiles(fs, gameDirectory, "*.bak"));
        ChecksContext.Equal("失败后没有留下 .tmp", 0, CountFiles(fs, gameDirectory, "*.tmp"));
        ChecksContext.Equal("失败后源文件仍在", true, fs.FileExists(sourcePath));
    }

    private static void CheckSidecarAtomicWrite(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] sidecar 走原子写");
        var fixture = CreateFixture(fs, logger, root, "sidecar-atomic");
        var counting = new CountingFileSystem(fs);
        var service = new SaveBackupService(counting, logger, fixture.DataRoot, fixture.Clock);
        var sourcePath = WriteSource(fixture, "atomic.ck3", [21, 22, 23]);
        var result = Await(service.BackupAsync(Ck3, sourcePath, null, CancellationToken.None));
        var gameDirectory = SaveBackupPaths.GameDirectory(fixture.DataRoot, Ck3);
        ChecksContext.Equal("原子写用例备份成功", SaveBackupStatus.Ok, result.Status);
        ChecksContext.Equal("sidecar 只调用一次 WriteAllTextAtomic", 1, counting.WriteAllTextAtomicCalls);
        var writtenPath = counting.WriteAllTextAtomicPaths.Count == 1
            ? counting.WriteAllTextAtomicPaths[0]
            : "(未写入)";
        ChecksContext.Equal("原子写目标就是该备份的 sidecar", result.Entry?.SidecarPath, writtenPath);
        ChecksContext.Check(
            "原子写目标以 .bak.json 结尾",
            writtenPath.EndsWith(SaveBackupPaths.BackupExtension + SaveBackupPaths.SidecarExtension, StringComparison.Ordinal),
            writtenPath);
        ChecksContext.Equal("原子写成功不留 .pdxnext.tmp", 0, CountFiles(fs, gameDirectory, "*.pdxnext.tmp"));
        ChecksContext.Equal("原子写成功目录里只有正文与 sidecar", 2, CountFiles(fs, gameDirectory, "*"));
    }

    private static void CheckSidecarWriteFailure(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[SaveBackupChecks] sidecar 原子写半截后抛 IOException");
        var fixture = CreateFixture(fs, logger, root, "sidecar-halfwrite");
        var failing = new HalfWrittenSidecarFileSystem(fs);
        var service = new SaveBackupService(failing, logger, fixture.DataRoot, fixture.Clock);
        var sourcePath = WriteSource(fixture, "half.ck3", [31, 32, 33, 34]);
        var result = Await(service.BackupAsync(Ck3, sourcePath, null, CancellationToken.None));
        var gameDirectory = SaveBackupPaths.GameDirectory(fixture.DataRoot, Ck3);
        ChecksContext.Equal("sidecar 写失败返回 Failed", SaveBackupStatus.Failed, result.Status);
        ChecksContext.Equal("失败后没有 *.json", 0, CountFiles(fs, gameDirectory, "*.json"));
        ChecksContext.Equal("失败后没有 *.bak", 0, CountFiles(fs, gameDirectory, "*.bak"));
        ChecksContext.Equal("失败后没有 *.tmp", 0, CountFiles(fs, gameDirectory, "*.tmp"));
        ChecksContext.Equal("失败后没有 *.pdxnext.tmp", 0, CountFiles(fs, gameDirectory, "*.pdxnext.tmp"));
        ChecksContext.Equal("失败后源存档仍在", true, fs.FileExists(sourcePath));
        ChecksContext.Equal("失败后 RemainingPaths 为空", 0, result.RemainingPaths.Count);
    }

    // ------------------------------------------------------------------
    // 工具
    // ------------------------------------------------------------------

    private static SaveGameMeta CreateMeta(string path, string fileName, long size) =>
        new(
            path,
            fileName,
            Ck3,
            SaveContainer.TextMeta,
            "神罗",
            "1066.9.15",
            "1.13.0",
            "Heinrich",
            false,
            "HRE",
            size,
            BaseTime,
            SaveParseState.Ok,
            null);

    private static Fixture CreateFixture(IFileSystem fs, IAppLogger logger, string root, string label)
    {
        var fixtureRoot = Path.Combine(root, "fixture-" + label);
        var dataRoot = Path.Combine(fixtureRoot, "data");
        var savesDirectory = Path.Combine(
            fixtureRoot,
            "Documents",
            "Paradox Interactive",
            "Crusader Kings III",
            "save games");
        fs.EnsureDirectory(savesDirectory);
        var clock = new FixedClock(BaseTime);
        var service = new SaveBackupService(fs, logger, dataRoot, clock);
        return new Fixture(fixtureRoot, dataRoot, savesDirectory, fs, logger, clock, service);
    }

    private static string WriteSource(Fixture fixture, string fileName, byte[] bytes)
    {
        var path = Path.Combine(fixture.SavesDirectory, fileName);
        fixture.FileSystem.EnsureDirectory(fixture.SavesDirectory);
        fixture.FileSystem.WriteAllBytesAtomic(path, bytes);
        return path;
    }

    private static int CountFiles(IFileSystem fs, string directory, string pattern)
    {
        if (!fs.DirectoryExists(directory))
        {
            return 0;
        }

        return fs.EnumerateFiles(directory, pattern, recursive: true).Count();
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool HasWellFormedSurrogates(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ThrowsCancellation(Func<Task> action)
    {
        try
        {
            action().GetAwaiter().GetResult();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private sealed record Fixture(
        string FixtureRoot,
        string DataRoot,
        string SavesDirectory,
        IFileSystem FileSystem,
        IAppLogger Logger,
        FixedClock Clock,
        SaveBackupService Service);

    /// <summary>把 <see cref="IFileSystem"/> 全部成员转给内层实现；子类只覆盖需要注入故障/计数的成员。</summary>
    private abstract class FileSystemWrapper : IFileSystem
    {
        protected FileSystemWrapper(IFileSystem inner) => Inner = inner;

        protected IFileSystem Inner { get; }

        public virtual bool FileExists(string path) => Inner.FileExists(path);

        public virtual long GetFileSize(string path) => Inner.GetFileSize(path);

        public virtual bool DirectoryExists(string path) => Inner.DirectoryExists(path);

        public virtual string ReadAllText(string path) => Inner.ReadAllText(path);

        public virtual byte[] ReadAllBytes(string path) => Inner.ReadAllBytes(path);

        public virtual Stream OpenRead(string path) => Inner.OpenRead(path);

        public virtual void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> data) =>
            Inner.WriteAllBytesAtomic(path, data);

        public virtual void WriteAllTextAtomic(string path, string text) => Inner.WriteAllTextAtomic(path, text);

        public virtual IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
            Inner.EnumerateFiles(directory, searchPattern, recursive);

        public virtual IEnumerable<string> EnumerateDirectories(string directory) => Inner.EnumerateDirectories(directory);

        public virtual string Combine(params string[] parts) => Inner.Combine(parts);

        public virtual string GetFullPath(string path) => Inner.GetFullPath(path);

        public virtual string? GetDirectoryName(string path) => Inner.GetDirectoryName(path);

        public virtual string GetFileName(string path) => Inner.GetFileName(path);

        public virtual void EnsureDirectory(string path) => Inner.EnsureDirectory(path);

        public virtual void Delete(string path) => Inner.Delete(path);

        public virtual void Copy(string source, string destination, bool overwrite) =>
            Inner.Copy(source, destination, overwrite);

        public virtual void Move(string source, string destination, bool overwrite) =>
            Inner.Move(source, destination, overwrite);

        public virtual void MoveDirectory(string source, string destination) =>
            Inner.MoveDirectory(source, destination);
    }

    /// <summary>包装真实文件系统，只让「备份正文落盘」的 Move 抛 IOException，用来验证半截文件不会落地。</summary>
    private sealed class FailingMoveFileSystem : FileSystemWrapper
    {
        public FailingMoveFileSystem(IFileSystem inner)
            : base(inner)
        {
        }

        public override void Move(string source, string destination, bool overwrite)
        {
            if (destination.EndsWith(SaveBackupPaths.BackupExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("注入的 Move 失败（模拟备份正文落盘失败）");
            }

            Inner.Move(source, destination, overwrite);
        }
    }

    /// <summary>包装真实文件系统，记录 <see cref="WriteAllTextAtomic"/> 的调用次数与目标路径。</summary>
    private sealed class CountingFileSystem : FileSystemWrapper
    {
        public CountingFileSystem(IFileSystem inner)
            : base(inner)
        {
        }

        public int WriteAllTextAtomicCalls { get; private set; }

        public List<string> WriteAllTextAtomicPaths { get; } = [];

        public override void WriteAllTextAtomic(string path, string text)
        {
            WriteAllTextAtomicCalls++;
            WriteAllTextAtomicPaths.Add(path);
            Inner.WriteAllTextAtomic(path, text);
        }
    }

    /// <summary>
    /// 包装真实文件系统：sidecar 的原子写先落半截（半个 JSON）再抛 IOException，
    /// 模拟「原子写内部写完一半就失败」，用来验证服务会清掉这半截 sidecar。
    /// </summary>
    private sealed class HalfWrittenSidecarFileSystem : FileSystemWrapper
    {
        public HalfWrittenSidecarFileSystem(IFileSystem inner)
            : base(inner)
        {
        }

        public override void WriteAllTextAtomic(string path, string text)
        {
            if (!path.EndsWith(SaveBackupPaths.SidecarExtension, StringComparison.OrdinalIgnoreCase))
            {
                Inner.WriteAllTextAtomic(path, text);
                return;
            }

            var half = text.Length > 2 ? text[..(text.Length / 2)] : text;
            Inner.WriteAllTextAtomic(path, half);
            throw new IOException("注入的 sidecar 原子写失败（模拟写到一半）");
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; set; }
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
}
