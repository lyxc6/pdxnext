namespace PdxLauncher.Checks;

using PdxLauncher.Achievements;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;

public static class AchievementConditionChecks
{
    public static int Run(IFileSystem fs)
    {
        var before = ChecksContext.Failures;
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-achievement-conditions-" + Guid.NewGuid().ToString("N"));

        try
        {
            var gameRoot = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(gameRoot, "common", "achievements"));
            File.WriteAllText(
                Path.Combine(gameRoot, "common", "achievements", "standard_achievements.txt"),
                """
                way_of_life_achievement = {
                    possible = {
                        ironman = yes
                    }
                    happened = {
                        custom_description = {
                            text = way_of_life_achievement_trigger
                            OR = {
                                has_perk = diplomat_perk
                                has_perk = schemer_perk
                            }
                        }
                    }
                }

                raw_only = {
                    happened = {
                        has_title = title:k_hungary
                    }
                }

                alias_key = {
                    localization = NEW_ACHIEVEMENT_1_2
                    happened = {
                        has_country_flag = have_diploannexed
                    }
                }

                indirect_tt = {
                    happened = {
                        custom_description = {
                            text = indirect_tt
                            has_title = title:k_hungary
                        }
                    }
                }

                unbalanced_achievement = {
                    happened = {
                        count >= 3
                
                """);
            Directory.CreateDirectory(Path.Combine(gameRoot, "localization", "simp_chinese"));
            File.WriteAllText(
                Path.Combine(gameRoot, "localization", "simp_chinese", "achievements_l_simp_chinese.yml"),
                "l_simp_chinese:\n"
                + " way_of_life_achievement_trigger: \"获得一套生活方式中的所有技能\"\n"
                + " indirect_tt: \"$way_of_life_achievement_trigger$\"\n");

            var reader = new AchievementConditionReader(new FileSystem());
            var localization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["way_of_life_achievement_trigger"] = "获得一套生活方式中的所有技能",
                ["indirect_tt"] = "$way_of_life_achievement_trigger$",
            };
            var hint = reader.Read(gameRoot, "way_of_life_achievement", localization);
            ChecksContext.Check("成就条件: 官方提示词命中", hint.IsOfficialHint && hint.Summary == "获得一套生活方式中的所有技能", hint.Summary);
            ChecksContext.Check("成就条件: 展开区显示解锁判定原文", hint.UnlockScript.Contains("has_perk = diplomat_perk", StringComparison.Ordinal), "[" + hint.UnlockScript + "]");
            ChecksContext.Check("成就条件: 展开区显示前置限制", hint.PossibleScript.Contains("ironman = yes", StringComparison.Ordinal), "[" + hint.PossibleScript + "]");
            ChecksContext.Check("成就条件: 记录来源文件", hint.SourceFile.EndsWith("standard_achievements.txt", StringComparison.OrdinalIgnoreCase), hint.SourceFile);

            var aliasHint = reader.Read(gameRoot, "way_of_life_achievement", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["way_of_life_achievement_trigger"] = "$indirect_tt$",
                ["indirect_tt"] = "获得一套生活方式中的所有技能",
            });
            ChecksContext.Check("成就条件: 整值本地化别名会被解析", aliasHint.IsOfficialHint && aliasHint.Summary == "获得一套生活方式中的所有技能", aliasHint.Summary);

            var raw = reader.Read(gameRoot, "raw_only", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            ChecksContext.Check(
                "成就条件: 无提示词时展示原始判定",
                !raw.IsOfficialHint && raw.UnlockScript.Contains("has_title = title:k_hungary", StringComparison.Ordinal),
                "[" + raw.UnlockScript + "]");

            var missing = reader.Read(gameRoot, "not_in_files", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            ChecksContext.Check("成就条件: 找不到时返回空而不是猜测", !missing.HasData && missing.Summary.Length == 0 && missing.UnlockScript.Length == 0);

            var broken = reader.Read(gameRoot, "unbalanced_achievement", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            ChecksContext.Check("成就条件: 未闭合块不会拖垮读取", !broken.HasData);

            ChecksContext.Check(
                "成就条件: 不做模糊匹配",
                !reader.Read(gameRoot, "way_of_life", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)).HasData);

            var byLocalization = reader.Read(gameRoot, "NEW_ACHIEVEMENT_1_2", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            ChecksContext.Check(
                "成就条件: localization 字段做精确映射",
                byLocalization.UnlockScript.Contains("has_country_flag = have_diploannexed", StringComparison.Ordinal),
                "[" + byLocalization.UnlockScript + "]");

            CheckCommentsDoNotHang(fs, root);
            CheckIndexBuiltOnce(fs, root);
            CheckTriggerLocalizationHint(fs, root);
            CheckDescriptionFallback(fs, root);
            CheckConceptLinksKeepText(fs, root);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 回归：成就脚本里出现 <c>#</c> 注释时不能原地打转。
    /// <para>
    /// 这是真实事故的根因 —— <c>SkipIgnorable</c> 调用的“跳到下一行”遇到行首 '#' 立刻 break，
    /// 索引不前进，CK3 的 12 份脚本里只要有一行注释，成就页就永远停在「加载中」。
    /// 这里把读取放到独立线程并限时，一旦再次死循环就是明确的失败，而不是把自检挂死。
    /// </para>
    /// </summary>
    private static void CheckCommentsDoNotHang(IFileSystem fs, string root)
    {
        var gameRoot = Path.Combine(root, "game-comments");
        Directory.CreateDirectory(Path.Combine(gameRoot, "game", "common", "achievements"));
        File.WriteAllText(
            Path.Combine(gameRoot, "game", "common", "achievements", "commented_achievements.txt"),
            """
            # 文件头注释
            commented_achievement = {
                # 块内注释
                possible = {
                    # 前置限制注释
                    ironman = yes
                }
                happened = {
                    custom_description = {
                        text = commented_achievement_trigger
                        has_title = title:k_hungary # 行尾注释
                    }
                }
            }
            """);

        var reader = new AchievementConditionReader(new FileSystem());
        var localization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["commented_achievement_trigger"] = "带上注释的成就条件",
        };

        var task = Task.Run(() => reader.Read(gameRoot, "commented_achievement", localization));
        if (!task.Wait(TimeSpan.FromSeconds(5)))
        {
            ChecksContext.Check("成就条件: 注释不会导致死循环", false, "读取超过 5 秒仍未返回");
            return;
        }

        var info = task.Result;
        ChecksContext.Check(
            "成就条件: 注释不会导致死循环",
            info.IsOfficialHint && info.Summary == "带上注释的成就条件",
            info.Summary);
        ChecksContext.Check(
            "成就条件: 注释不切断原始判定",
            info.UnlockScript.Contains("has_title = title:k_hungary", StringComparison.Ordinal),
            "[" + info.UnlockScript + "]");
    }

    /// <summary>
    /// 回归：同一个安装目录只扫一次脚本。
    /// <para>
    /// 成就页一次刷新要查几百条成就，每条都重读 12 份脚本会把界面拖住几秒。
    /// 用计数文件系统钉住「读文件次数」：第一次读取 = 脚本数，之后的读取必须为 0。
    /// </para>
    /// </summary>
    private static void CheckIndexBuiltOnce(IFileSystem fs, string root)
    {
        var gameRoot = Path.Combine(root, "game-index");
        var scripts = Path.Combine(gameRoot, "game", "common", "achievements");
        Directory.CreateDirectory(scripts);
        const int scriptCount = 3;
        for (var i = 0; i < scriptCount; i++)
        {
            File.WriteAllText(
                Path.Combine(scripts, "indexed_" + i + ".txt"),
                "indexed_achievement_" + i + " = {\n"
                + "    # 注释\n"
                + "    happened = {\n"
                + "        has_country_flag = flag_" + i + "\n"
                + "    }\n"
                + "}\n");
        }

        var counting = new CountingFileSystem(fs);
        var reader = new AchievementConditionReader(counting);
        var localization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var first = reader.Read(gameRoot, "indexed_achievement_0", localization);
        var readsAfterFirst = counting.ReadCount;
        ChecksContext.Check(
            "成就条件: 首次读取建立索引（每次文件一个）",
            readsAfterFirst == scriptCount,
            "读取 " + readsAfterFirst + " 次，脚本 " + scriptCount + " 份");

        var second = reader.Read(gameRoot, "indexed_achievement_1", localization);
        var readsAfterSecond = counting.ReadCount;
        ChecksContext.Check(
            "成就条件: 同一目录后续读取不再重扫脚本",
            readsAfterSecond == readsAfterFirst,
            "新增读取 " + (readsAfterSecond - readsAfterFirst) + " 次");
        ChecksContext.Check(
            "成就条件: 索引读取仍能命中不同块",
            first.UnlockScript.Contains("flag_0", StringComparison.Ordinal)
            && second.UnlockScript.Contains("flag_1", StringComparison.Ordinal),
            "[" + first.UnlockScript + "] / [" + second.UnlockScript + "]");
    }

    /// <summary>
    /// 官方提示词的两级跳转：成就脚本里的 <c>text = xxx_trigger</c> 指向
    /// <c>common/trigger_localization</c> 的 <c>global = ACHIEVEMENT_DESC_xxx</c>，再指向 yml 文案。
    /// 只查一层时真实 CK3 的 218 条成就提示词会全部落空（页面上只剩原始判定）。
    /// </summary>
    private static void CheckTriggerLocalizationHint(IFileSystem fs, string root)
    {
        var gameRoot = Path.Combine(root, "game-hint");
        var achievements = Path.Combine(gameRoot, "game", "common", "achievements");
        var triggers = Path.Combine(gameRoot, "game", "common", "trigger_localization");
        Directory.CreateDirectory(achievements);
        Directory.CreateDirectory(triggers);
        File.WriteAllText(
            Path.Combine(achievements, "hinted_achievements.txt"),
            """
            # 注释
            hinted_achievement = {
                happened = {
                    custom_description = {
                        text = hinted_achievement_trigger
                        has_country_flag = hinted_flag
                    }
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(triggers, "00_achievement_triggers.txt"),
            """
            hinted_achievement_trigger = {
                global = ACHIEVEMENT_DESC_hinted_achievement
            }
            """);

        var reader = new AchievementConditionReader(fs);
        var localization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACHIEVEMENT_DESC_hinted_achievement"] = "用提示词描述的成就条件",
        };
        var info = reader.Read(gameRoot, "hinted_achievement", localization);
        ChecksContext.Check(
            "成就条件: trigger_localization 提示键会跳转到本地化",
            info.IsOfficialHint && info.Summary == "用提示词描述的成就条件",
            info.Summary);
    }

    /// <summary>
    /// 没有 text / tooltip 时退回 <c>ACHIEVEMENT_DESC_&lt;api&gt;</c>：
    /// 很多基础成就的脚本只有裸判定，官方文案只存在于本地化里。
    /// </summary>
    private static void CheckDescriptionFallback(IFileSystem fs, string root)
    {
        var gameRoot = Path.Combine(root, "game-desc");
        var achievements = Path.Combine(gameRoot, "game", "common", "achievements");
        Directory.CreateDirectory(achievements);
        File.WriteAllText(
            Path.Combine(achievements, "desc_achievements.txt"),
            """
            # 只有裸判定的成就
            desc_achievement = {
                happened = {
                    has_title = title:k_hungary
                }
            }
            """);

        var reader = new AchievementConditionReader(fs);
        var localization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACHIEVEMENT_DESC_desc_achievement"] = "官方描述文案",
        };
        var info = reader.Read(gameRoot, "desc_achievement", localization);
        ChecksContext.Check(
            "成就条件: 无脚本提示时退回成就描述",
            info.IsOfficialHint && info.Summary == "官方描述文案",
            info.Summary);
    }

    /// <summary>
    /// CK3 的提示词里会混 <c>[lifestyle|E]</c> 这种概念链接；它们是句子的组成词，
    /// 不能当标记整段剥掉（会把「获得一套[生活方式]里的所有技能」变成残句）。
    /// </summary>
    private static void CheckConceptLinksKeepText(IFileSystem fs, string root)
    {
        var gameRoot = Path.Combine(root, "game-links");
        var achievements = Path.Combine(gameRoot, "game", "common", "achievements");
        Directory.CreateDirectory(achievements);
        File.WriteAllText(
            Path.Combine(achievements, "linked_achievements.txt"),
            """
            # 注释
            linked_achievement = {
                happened = {
                    custom_description = {
                        text = linked_achievement_trigger
                        has_country_flag = linked_flag
                    }
                }
            }
            """);

        var reader = new AchievementConditionReader(fs);
        var localization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["linked_achievement_trigger"] = "获得一套[lifestyle|E]里的#EMP 所有#!技能",
        };
        var info = reader.Read(gameRoot, "linked_achievement", localization);
        ChecksContext.Check(
            "成就条件: 概念链接保留可读词",
            info.IsOfficialHint && info.Summary.Contains("lifestyle", StringComparison.OrdinalIgnoreCase)
            && info.Summary.Contains("所有", StringComparison.Ordinal)
            && !info.Summary.Contains('[', StringComparison.Ordinal)
            && !info.Summary.Contains('#', StringComparison.Ordinal)
            && !info.Summary.Contains("§", StringComparison.Ordinal),
            info.Summary);

        // 真实 CK3：裸键 `legend_protagonist` 被脚本表达式占用
        // （`legend_protagonist = [Legend.GetProtagonist.GetName]`），概念名只在 game_concept_* 里。
        // 直接信裸键会把主语整个丢掉，只剩「为你的主持葬礼。」
        var shadowed = reader.Read(
            gameRoot,
            "linked_achievement",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["linked_achievement_trigger"] = "为你的[legend_protagonist|E]主持葬礼。",
                ["legend_protagonist"] = "[Legend.GetProtagonist.GetName]",
                ["game_concept_legend_protagonist"] = "传奇主角",
            });
        ChecksContext.Check(
            "成就条件: 概念名优先于被脚本占用的裸键",
            shadowed.IsOfficialHint && shadowed.Summary.Contains("传奇主角", StringComparison.Ordinal)
            && shadowed.Summary.Contains("葬礼", StringComparison.Ordinal),
            shadowed.Summary);
    }

    /// <summary>只统计 <see cref="ReadAllText"/> 次数的文件系统包装。</summary>
    private sealed class CountingFileSystem(IFileSystem inner) : IFileSystem
    {
        private int _reads;

        public int ReadCount => Volatile.Read(ref _reads);

        public bool FileExists(string path) => inner.FileExists(path);

        public long GetFileSize(string path) => inner.GetFileSize(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path)
        {
            Interlocked.Increment(ref _reads);
            return inner.ReadAllText(path);
        }

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public Stream OpenRead(string path) => inner.OpenRead(path);

        public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> data) => inner.WriteAllBytesAtomic(path, data);

        public void WriteAllTextAtomic(string path, string text) => inner.WriteAllTextAtomic(path, text);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
            inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

        public string Combine(params string[] parts) => inner.Combine(parts);

        public string GetFullPath(string path) => inner.GetFullPath(path);

        public string? GetDirectoryName(string path) => inner.GetDirectoryName(path);

        public string GetFileName(string path) => inner.GetFileName(path);

        public void EnsureDirectory(string path) => inner.EnsureDirectory(path);

        public void Delete(string path) => inner.Delete(path);

        public void Copy(string source, string destination, bool overwrite) => inner.Copy(source, destination, overwrite);

        public void CopyDirectory(string source, string destination, bool overwrite, CancellationToken cancellationToken = default) =>
            inner.CopyDirectory(source, destination, overwrite, cancellationToken);

        public void Move(string source, string destination, bool overwrite) => inner.Move(source, destination, overwrite);

        public void MoveDirectory(string source, string destination) => inner.MoveDirectory(source, destination);
    }
}
