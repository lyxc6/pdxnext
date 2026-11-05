namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Saves;
using System.IO.Compression;

/// <summary>
/// 自检：存档容器头解析。用 <c>tests/fixtures/saves</c> 里自造的小样本钉住
/// CK3 文本 meta、Victoria 3 / EU5 二进制 token、Stellaris zip meta、CS2 的 .cok zip
/// 以及损坏降级与「只读前 4 KB」的口径。
/// </summary>
public static class SavContainerReaderChecks
{
    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var reader = new SavContainerReader(fs, logger);

        CheckCk3(fs, reader);
        CheckVictoria3(reader);
        CheckVictoria3ZipNamedSave(fs, reader);
        CheckVictoria3ZipWithoutMeta(fs, reader);
        CheckEu5(reader);
        CheckStellaris(reader);
        CheckCs2(reader);
        CheckCs2TopLevelVersionWins(fs, reader);
        CheckCorrupt(reader);
        CheckBoundedRead(fs, reader);
        CheckMissingFile(fs, reader);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckCk3(IFileSystem fs, SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] CK3 文本 meta_data");
        var path = FixturePath("ck3/crusader_king_iii.ck3");
        var meta = reader.Parse("ck3", path, SaveContainer.TextMeta);

        ChecksContext.Equal("CK3 容器", SaveContainer.TextMeta, meta.Container);
        ChecksContext.Equal("CK3 解析状态", SaveParseState.Ok, meta.ParseState);
        ChecksContext.Equal("CK3 版本", "1.19.0.6", meta.GameVersion ?? "<null>");
        ChecksContext.Equal("CK3 游戏内日期", "1066.9.29", meta.GameDate ?? "<null>");
        ChecksContext.Equal("CK3 存档名", "阿尔弗雷德大帝", meta.SaveName ?? "<null>");
        ChecksContext.Equal("CK3 玩家名", "Alfred", meta.PlayerName ?? "<null>");
        ChecksContext.Equal("CK3 国家", "英格兰", meta.PlayerCountry ?? "<null>");
        ChecksContext.Check("CK3 铁人标记", meta.IsIronman);
        ChecksContext.Equal("CK3 文件大小来自磁盘", fs.GetFileSize(path), meta.FileSize);
    }

    private static void CheckVictoria3(SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] Victoria 3 二进制 token");
        var path = FixturePath("victoria3/大不列颠_1850_06_18.v3");
        var meta = reader.Parse("victoria3", path, SaveContainer.BinaryToken);

        ChecksContext.Equal("V3 容器", SaveContainer.BinaryToken, meta.Container);
        ChecksContext.Equal("V3 解析状态", SaveParseState.Ok, meta.ParseState);
        ChecksContext.Equal("V3 版本", "1.13.11", meta.GameVersion ?? "<null>");
        ChecksContext.Equal("V3 日期回退到文件名", "1850.06.18", meta.GameDate ?? "<null>");
        ChecksContext.Equal("V3 存档名去掉日期后缀", "大不列颠", meta.SaveName ?? "<null>");
        ChecksContext.Equal("V3 国家（嵌套字符串不参与）", "大不列颠", meta.PlayerCountry ?? "<null>");
        ChecksContext.Equal("V3 文件名中文原样保留", "大不列颠_1850_06_18.v3", meta.FileName);
    }

    private static void CheckVictoria3ZipNamedSave(IFileSystem fs, SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] Victoria 3 命名档 = SAV0 + zip(meta=token)");

        // 合成样本：SAV0 + id + \n + zip；meta 里是 Jomini token 流。绝不使用玩家真实存档。
        var root = PlaysetChecks.CreateTempRoot(fs, "saves-v3zip");
        try
        {
            var path = Path.Combine(root, "合成战役_1848_03_02.v3");
            fs.WriteAllBytesAtomic(path, BuildV3ZipSave("meta"));

            var meta = reader.Parse("victoria3", path, SaveContainer.BinaryToken);
            ChecksContext.Equal("V3 zip 容器", SaveContainer.BinaryToken, meta.Container);
            ChecksContext.Equal("V3 zip 解析状态", SaveParseState.Ok, meta.ParseState);
            ChecksContext.Equal("V3 zip 版本来自 meta token", "1.99.0", meta.GameVersion ?? "<null>");
            ChecksContext.Equal("V3 zip 存档名来自 meta token", "合成战役", meta.SaveName ?? "<null>");
            ChecksContext.Equal("V3 zip 游戏内日期来自 meta token", "1848.03.02", meta.GameDate ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckVictoria3ZipWithoutMeta(IFileSystem fs, SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] Victoria 3 zip 缺少 meta 时降级");
        var root = PlaysetChecks.CreateTempRoot(fs, "saves-v3zip-empty");
        try
        {
            var path = Path.Combine(root, "无元数据_1848_03_02.v3");
            fs.WriteAllBytesAtomic(path, BuildV3ZipSave(entryName: null));

            var meta = reader.Parse("victoria3", path, SaveContainer.BinaryToken);
            ChecksContext.Equal("缺少 meta 的样本档降级", SaveParseState.Degraded, meta.ParseState);
            ChecksContext.Check("缺少 meta 时记录原因", !string.IsNullOrWhiteSpace(meta.DegradeReason), meta.DegradeReason);
            ChecksContext.Equal("降级仍用文件名日期", "1848.03.02", meta.GameDate ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static byte[] BuildV3ZipSave(string? entryName)
    {
        using var body = new MemoryStream();
        using (var archive = new ZipArchive(body, ZipArchiveMode.Create, leaveOpen: true))
        {
            var gamestate = archive.CreateEntry("gamestate", CompressionLevel.NoCompression);
            using (var gamestateStream = gamestate.Open())
            {
                gamestateStream.Write([0x00, 0x01, 0x02, 0x03]);
            }

            if (entryName is not null)
            {
                var meta = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                using var metaStream = meta.Open();
                metaStream.Write(BuildV3MetaTokenStream());
            }
        }

        var header = System.Text.Encoding.ASCII.GetBytes("SAV0synthetic-save-id\n");
        var zip = body.ToArray();
        var result = new byte[header.Length + zip.Length];
        header.CopyTo(result, 0);
        zip.CopyTo(result, header.Length);
        return result;
    }

    private static byte[] BuildV3MetaTokenStream()
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, 0x0003); // OPEN（深度 1，只有这一层的字符串会被采集）
        WriteUInt16(stream, 0x1234); // 普通键
        WriteTokenString(stream, "1.99.0");
        WriteUInt16(stream, 0x1235);
        WriteTokenString(stream, "1848.03.02 - 合成战役");
        WriteUInt16(stream, 0x0004); // CLOSE
        return stream.ToArray();
    }

    private static void WriteTokenString(Stream stream, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WriteUInt16(stream, 0x000f);
        WriteUInt16(stream, (ushort)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)(value >> 8));
    }

    private static void CheckEu5(SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] EU5 二进制 token");
        var path = FixturePath("eu5/英格家_1337_04_14.eu5");
        var meta = reader.Parse("eu5", path, SaveContainer.BinaryToken);

        ChecksContext.Equal("EU5 容器", SaveContainer.BinaryToken, meta.Container);
        ChecksContext.Equal("EU5 解析状态", SaveParseState.Ok, meta.ParseState);
        ChecksContext.Equal("EU5 版本", "1.4.5", meta.GameVersion ?? "<null>");
        ChecksContext.Equal("EU5 日期来自「日期 - 名字」字符串", "1337.4.14", meta.GameDate ?? "<null>");
        ChecksContext.Equal("EU5 存档名来自「日期 - 名字」", "英格家", meta.SaveName ?? "<null>");
        ChecksContext.Equal("EU5 玩家名去掉 #hash", "英格家", meta.PlayerName ?? "<null>");
        ChecksContext.Equal("EU5 国家", "英格家", meta.PlayerCountry ?? "<null>");
    }

    private static void CheckStellaris(SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] Stellaris zip meta");
        var path = FixturePath("stellaris/凯尔赞枢纽_2210_06_17.sav");
        var meta = reader.Parse("stellaris", path, SaveContainer.ZipMeta);

        ChecksContext.Equal("Stellaris 容器", SaveContainer.ZipMeta, meta.Container);
        ChecksContext.Equal("Stellaris 解析状态", SaveParseState.Ok, meta.ParseState);
        ChecksContext.Equal("Stellaris 版本", "Corvus v4.2.2", meta.GameVersion ?? "<null>");
        ChecksContext.Equal("Stellaris 日期", "2210.06.17", meta.GameDate ?? "<null>");
        ChecksContext.Equal("Stellaris 存档名", "凯尔赞枢纽 2", meta.SaveName ?? "<null>");
        ChecksContext.Equal("Stellaris 玩家名", "Kael", meta.PlayerName ?? "<null>");
        ChecksContext.Check("Stellaris 铁人标记", meta.IsIronman);
    }

    private static void CheckCs2(SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] CS2 .cok zip（JSON 顶层元数据）");
        var path = FixturePath("cs2/阿伦德尔 2.cok");
        var meta = reader.Parse("cities_skylines_2", path, SaveContainer.Colossal);

        ChecksContext.Equal("CS2 容器", SaveContainer.Colossal, meta.Container);
        ChecksContext.Equal("CS2 解析状态", SaveParseState.Ok, meta.ParseState);
        ChecksContext.Equal("CS2 存档名含中文与空格", "阿伦德尔 2", meta.SaveName ?? "<null>");
        ChecksContext.Equal("CS2 日期来自 simulationDate", "2024.10.01", meta.GameDate ?? "<null>");
        ChecksContext.Check("CS2 不从 modsEnabled 取版本", meta.GameVersion is null, meta.GameVersion);
    }

    private static void CheckCs2TopLevelVersionWins(IFileSystem fs, SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] CS2 顶层版本优先于 modsEnabled 干扰项");
        var root = PlaysetChecks.CreateTempRoot(fs, "saves-cs2-json");
        try
        {
            var path = Path.Combine(root, "顶层版本.cok");
            const string json = """
                {
                  "cityName": "顶层版本",
                  "version": "9.9.9f1",
                  "simulationDate": { "year": 2030, "month": 1, "day": 2 },
                  "modsEnabled": [
                    "Decoy, Version=0.8.3.0, Culture=neutral, PublicKeyToken=null"
                  ]
                }
                """;
            fs.WriteAllBytesAtomic(path, BuildCokZip("顶层版本.SaveGameMetadata", json));

            var meta = reader.Parse("cities_skylines_2", path, SaveContainer.Colossal);
            ChecksContext.Equal("CS2 顶层版本优先", "9.9.9f1", meta.GameVersion ?? "<null>");
            ChecksContext.Equal("CS2 顶层日期", "2030.01.02", meta.GameDate ?? "<null>");
            ChecksContext.Check(
                "CS2 版本不带 mods 的逗号尾巴",
                meta.GameVersion is not null && !meta.GameVersion.Contains(','),
                meta.GameVersion);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static byte[] BuildCokZip(string entryName, string json)
    {
        using var body = new MemoryStream();
        using (var archive = new ZipArchive(body, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write([0xEF, 0xBB, 0xBF]);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
        }

        return body.ToArray();
    }

    private static void CheckCorrupt(SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] 损坏档降级");
        var path = FixturePath("corrupt/broken.ck3");
        var meta = reader.Parse("ck3", path, SaveContainer.TextMeta);

        ChecksContext.Equal("损坏档状态为 Degraded", SaveParseState.Degraded, meta.ParseState);
        ChecksContext.Check("损坏档有降级原因", !string.IsNullOrWhiteSpace(meta.DegradeReason), meta.DegradeReason);
        ChecksContext.Equal("损坏档仍用文件名作存档名", "broken", meta.SaveName ?? "<null>");
        ChecksContext.Check("损坏档不编造版本", meta.GameVersion is null, meta.GameVersion);
        ChecksContext.Check("损坏档不编造日期", meta.GameDate is null, meta.GameDate);
    }

    private static void CheckBoundedRead(IFileSystem fs, SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] 超大文件只读前 4 KB");
        var root = PlaysetChecks.CreateTempRoot(fs, "saves-bounded");
        try
        {
            var path = Path.Combine(root, "huge.ck3");
            var data = new byte[2 * 1024 * 1024];
            Array.Fill(data, (byte)'A');
            "SAV0"u8.CopyTo(data);
            data[4] = (byte)'\n';
            const string outOfRangeMeta = "meta_data={ version=\"9.9.9\" meta_date=1.1.1 meta_title_name=\"越界\" }";
            var marker = System.Text.Encoding.ASCII.GetBytes(outOfRangeMeta);
            marker.CopyTo(data, 8192);
            fs.WriteAllBytesAtomic(path, data);

            var meta = reader.Parse("ck3", path, SaveContainer.TextMeta);
            ChecksContext.Equal("2 MB 文件仍上报真实大小", (long)data.Length, meta.FileSize);
            ChecksContext.Equal("4 KB 之外的 meta_data 不被读取", SaveParseState.Degraded, meta.ParseState);
            ChecksContext.Check("4 KB 之外的存档名不生效", meta.SaveName == "huge", meta.SaveName);
            ChecksContext.Check("4 KB 之外的版本不生效", meta.GameVersion is null, meta.GameVersion);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckMissingFile(IFileSystem fs, SavContainerReader reader)
    {
        Console.WriteLine("[SavContainerReaderChecks] 缺失文件不抛异常");
        var path = Path.Combine(Path.GetTempPath(), "pdxnext-missing-" + Guid.NewGuid().ToString("N") + ".ck3");
        var meta = reader.Parse("ck3", path, SaveContainer.TextMeta);

        ChecksContext.Equal("缺失文件降级", SaveParseState.Degraded, meta.ParseState);
        ChecksContext.Equal("缺失文件大小为 0", 0L, meta.FileSize);
        ChecksContext.Check(
            "缺失文件名仍可展示",
            meta.SaveName is not null &&
            meta.SaveName.StartsWith("pdxnext-missing", StringComparison.Ordinal),
            meta.SaveName);
        ChecksContext.Check("缺失文件记录降级原因", !string.IsNullOrWhiteSpace(meta.DegradeReason), meta.DegradeReason);
    }

    private static string FixturePath(string relative) => ChecksContext.FixturePath("saves/" + relative);

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
