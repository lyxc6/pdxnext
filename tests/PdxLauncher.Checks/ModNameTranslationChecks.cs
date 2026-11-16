namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online.Translation;
using PdxLauncher.Core.Storage;

/// <summary>
/// mod 名称译名管道：回填条件、落盘列表、目标语言判定，以及「三处页面共用同一张表」的口径。
/// <para>
/// 这套东西错了不会崩、不会报错，只表现为「同一个 mod 在 Mod 库有中文名、在添加模组页没有」，
/// 用户只能靠肉眼比对三个页面才发现，所以这里把每一条口径都钉住。
/// </para>
/// </summary>
public static class ModNameTranslationChecks
{
    /// <summary>跑一遍全部译名管道检查；返回失败条数。</summary>
    /// <param name="fs">文件系统（本检查只读源码文本，不用它落盘）。</param>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();
        CheckBackfill();
        CheckTargetLanguage();
        CheckRoundTrip();
        CheckPagesSharePipeline(root);
        return ChecksContext.Failures - before;
    }

    /// <summary>回填：同 ModId + 同目标语言 + 原文没变才命中；任一不满足就当没有译名。</summary>
    private static void CheckBackfill()
    {
        var translator = new ModNameTranslator(() => TranslationOptions.SimplifiedChinese);
        var modId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        translator.Remember(modId, "Canals Add Company Capacity", TranslationOptions.SimplifiedChinese, "运河增加公司容量");

        ChecksContext.Check(
            "译名: 同一 ModId + 同一语言 + 原文未变时命中回填",
            translator.TryBackfill(modId, "Canals Add Company Capacity", TranslationOptions.SimplifiedChinese, out var hit));
        ChecksContext.Equal("译名: 命中时拿到的就是那句译文", "运河增加公司容量", hit);

        ChecksContext.Check(
            "译名: 原文改过就不命中（旧译文不能贴到新名字上）",
            !translator.TryBackfill(modId, "Canals Add Company Capacity v2", TranslationOptions.SimplifiedChinese, out _));
        ChecksContext.Check(
            "译名: 目标语言不同就不命中",
            !translator.TryBackfill(modId, "Canals Add Company Capacity", TranslationOptions.English, out _));
        ChecksContext.Check(
            "译名: 名称翻译关闭（目标语言 null）就不命中",
            !translator.TryBackfill(modId, "Canals Add Company Capacity", null, out _));
        ChecksContext.Check(
            "译名: 没记过的 ModId 不命中",
            !translator.TryBackfill(Guid.NewGuid(), "Canals Add Company Capacity", TranslationOptions.SimplifiedChinese, out _));

        // 空译文 / 空原文一律不收：写进去会让界面显示一个空框，比不显示更像坏了。
        translator.Remember(modId, "   ", TranslationOptions.SimplifiedChinese, "有译文");
        translator.Remember(modId, "有名字", TranslationOptions.SimplifiedChinese, "   ");
        ChecksContext.Check(
            "译名: 空原文或空译文不覆盖已有条目",
            translator.TryBackfill(modId, "Canals Add Company Capacity", TranslationOptions.SimplifiedChinese, out var stillOld)
                && stillOld == "运河增加公司容量");

        translator.Clear();
        ChecksContext.Equal("译名: Clear 之后不再命中", 0, translator.Count);
    }

    /// <summary>目标语言判定：两个开关都开才翻译，否则返回 null（不能把「关着」当成「翻译失败」）。</summary>
    private static void CheckTargetLanguage()
    {
        var on = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateTargetLanguage = TranslationOptions.SimplifiedChinese },
            "zh-CN");
        var off = on with { Enabled = false };

        ChecksContext.Equal(
            "译名: 两个开关都开时给出目标语言",
            TranslationOptions.SimplifiedChinese,
            ModNameTranslator.ResolveTargetLanguage(true, on));
        ChecksContext.Check(
            "译名: 「翻译名称」关着时不翻译",
            ModNameTranslator.ResolveTargetLanguage(false, on) is null);
        ChecksContext.Check(
            "译名: 翻译总开关关着时不翻译",
            ModNameTranslator.ResolveTargetLanguage(true, off) is null);
        ChecksContext.Equal(
            "译名: 目标语言跟随设置（中文界面给 zh-CN）",
            TranslationOptions.SimplifiedChinese,
            ModNameTranslator.ResolveTargetLanguage(true, on));

        var translator = new ModNameTranslator(() => null);
        ChecksContext.Equal("译名: TargetLanguage 为 null 时 Count 为 0", 0, translator.Count);
    }

    /// <summary>
    /// 与真实 <see cref="TranslationCache"/> 往返：落盘 → 读回 → 回填命中。
    /// 这条越过「内存字典自己对」的层面，钉的是「换一页 / 换一次启动还认得这个译名」。
    /// </summary>
    private static void CheckRoundTrip()
    {
        var logger = new RecordingLogger();
        var documents = new MemoryDocumentStore();
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero));
        var cache = new TranslationCache(documents, clock, logger);
        var modId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        const string sourceName = "Better Greece";

        Func<string, CancellationToken, Task<IReadOnlyDictionary<Guid, TranslationCache.NameTranslation>>> load =
            (language, token) => cache.GetNameTranslationsAsync(language, token);

        var writer = new ModNameTranslator(
            () => TranslationOptions.SimplifiedChinese,
            load: load,
            save: (language, updates, token) => cache.SetNameTranslationsAsync(language, updates, token));
        writer.Remember(modId, sourceName, TranslationOptions.SimplifiedChinese, "更好的希腊");
        writer.SaveAsync(TranslationOptions.SimplifiedChinese, CancellationToken.None).GetAwaiter().GetResult();

        var reader = new ModNameTranslator(() => TranslationOptions.SimplifiedChinese, load: load);
        reader.LoadAsync(TranslationOptions.SimplifiedChinese, CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Check(
            "译名: 写盘后换一个管道实例也能回填（跨页面 / 跨启动复用）",
            reader.TryBackfill(modId, sourceName, TranslationOptions.SimplifiedChinese, out var hit)
                && hit == "更好的希腊");

        // 换目标语言读：英文管道读不到中文那批（表按 ModId + 语言分开存）。
        var english = new ModNameTranslator(() => TranslationOptions.English, load: load);
        english.LoadAsync(TranslationOptions.English, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "译名: 换目标语言读不到别的语言的译名",
            !english.TryBackfill(modId, sourceName, TranslationOptions.English, out _));

        // 落盘列表只带当前语言的条目，且字段完整（缺原文的话缓存层会跳过它）。
        var updates = writer.BuildUpdates(TranslationOptions.SimplifiedChinese);
        ChecksContext.Equal("译名: 落盘列表条数", 1, updates.Count);
        ChecksContext.Check(
            "译名: 落盘条目字段完整（ModId / 原文 / 语言 / 译文）",
            updates.Count == 1
                && updates[0].ModId == modId
                && updates[0].SourceName == sourceName
                && updates[0].TargetLanguage == TranslationOptions.SimplifiedChinese
                && updates[0].Translation == "更好的希腊");
        ChecksContext.Equal(
            "译名: 目标语言为 null 时不产生落盘条目（关着翻译不该写表）",
            0,
            writer.BuildUpdates(null).Count);
    }

    /// <summary>
    /// 三处页面必须都走这条管道：否则某页退回「只调引擎、不读写 Names 表」，
    /// 同一个 mod 就会在不同页面显示不同译文，用户看到的就是「翻译不一致」。
    /// </summary>
    private static void CheckPagesSharePipeline(string root)
    {
        (string Title, string Path)[] pages =
        [
            ("Mod 库", Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "ModLibraryViewModel.cs")),
            ("播放集页", Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "ModsViewModel.cs")),
            ("添加模组页", Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "AddModsViewModel.cs")),
        ];

        foreach (var (title, path) in pages)
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            ChecksContext.Check(
                "译名: " + title + " 用共享译名管道",
                text.Contains("ModNameTranslator", StringComparison.Ordinal));
            ChecksContext.Check(
                "译名: " + title + " 落盘走管道 SaveAsync（不自己拼一份表）",
                text.Contains(".SaveAsync(", StringComparison.Ordinal));
            ChecksContext.Check(
                "译名: " + title + " 回填走管道 TryBackfill",
                text.Contains(".TryBackfill(", StringComparison.Ordinal));
        }

        // 播放集页左栏候选行也必须带上游戏版本，否则候选永远不判版本支持（三角永不出现）。
        var modsVm = File.Exists(pages[1].Path) ? File.ReadAllText(pages[1].Path) : string.Empty;
        ChecksContext.Check(
            "译名: 播放集页左栏候选行带游戏版本（三角才有判定依据）",
            modsVm.Contains("gameVersion: _game?.Version", StringComparison.Ordinal));
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "i18n")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>内存文档库：替掉真实磁盘，其余读写口径不变。</summary>
    private sealed class MemoryDocumentStore : IJsonDocumentStore
    {
        private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

        public bool TryRead(string key, out string? json)
        {
            if (_documents.TryGetValue(key, out var value))
            {
                json = value;
                return true;
            }

            json = null;
            return false;
        }

        public bool Write(string key, string json)
        {
            _documents[key] = json;
            return true;
        }

        public bool Delete(string key) => _documents.Remove(key);

        public bool Exists(string key) => _documents.ContainsKey(key);

        public long MeasureBytes(string prefix)
        {
            long total = 0;
            foreach (var pair in _documents)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    total += pair.Value.Length;
                }
            }

            return total;
        }

        public IReadOnlyList<string> List(string prefix) =>
            _documents.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
    }

    /// <summary>可控时钟：缓存过期判定要能拨时间，不跟着真实时间跑。</summary>
    private sealed class MutableClock : IClock
    {
        private DateTimeOffset _now;

        public MutableClock(DateTimeOffset start) => _now = start;

        public DateTimeOffset UtcNow => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    /// <summary>只记日志不输出：检查里不希望每条 warning 都刷屏。</summary>
    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Warnings { get; } = new();

        public void Info(string message) { }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null)
        {
            if (exception is not null)
            {
                Warnings.Add(message + ": " + exception.Message);
            }
        }
    }
}
