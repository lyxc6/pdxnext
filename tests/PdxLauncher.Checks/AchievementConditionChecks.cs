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
}
