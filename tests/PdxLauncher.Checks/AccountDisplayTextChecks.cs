namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 账号页身份展示规则自检：头像首字母、名字回落、显示名是否等于邮箱、以及本次新增的两条状态文案键。
/// <para>
/// 为什么值得钉：这些规则错了不会崩、不会报错，只会在真机上表现为「看起来不对劲」——
/// 头像上出现一个「1」（像步骤序号）、状态行把同一个邮箱写两遍、已登录却写着「未登录」。
/// 用户能看见，但编译器看不见，所以口径放在 <see cref="AccountDisplayText"/> 里由这里钉住。
/// </para>
/// </summary>
public static class AccountDisplayTextChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckInitials();
        CheckProfileName();
        CheckSameIdentity();
        CheckStatusSessionKeys();

        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 首字母：跳过数字与符号在「显示名 / 邮箱本地段」里找第一个字母；找不到就返回空串
    /// （空串会让 PersonPicture 画通用人像，比一个假的字母准确）。
    /// </summary>
    private static void CheckInitials()
    {
        // 这次真机上的根因：Paradox 的 DisplayName 就是邮箱，纯数字本地段被取成「1」。
        ChecksContext.Equal("账号页: 纯数字邮箱 → 不留首字母（用人像占位）", string.Empty, AccountDisplayText.Initials("1714769724@qq.com", "1714769724@qq.com"));
        ChecksContext.Equal("账号页: 显示名优先", "A", AccountDisplayText.Initials("Alice", "a@b.c"));
        ChecksContext.Equal("账号页: 显示名首尾空白被忽略", "B", AccountDisplayText.Initials("  bob ", null));
        ChecksContext.Equal("账号页: 显示名无字母 → 退到本地段", "K", AccountDisplayText.Initials("12345", "Kim@Example.COM"));
        ChecksContext.Equal("账号页: 不拿域名凑首字母", string.Empty, AccountDisplayText.Initials("12345", "12345@123.com"));
        ChecksContext.Equal("账号页: 什么都没有 → 空串", string.Empty, AccountDisplayText.Initials(null, null));
        ChecksContext.Equal("账号页: 全空白 → 空串", string.Empty, AccountDisplayText.Initials("   ", "  "));
        ChecksContext.Equal("账号页: 中文显示名保留汉字", "张", AccountDisplayText.Initials("张三", null));
        ChecksContext.Equal("账号页: 小写字母转大写", "Q", AccountDisplayText.Initials(null, "qq@example.com"));
    }

    /// <summary>名字：显示名 → 邮箱 → 兜底文案（已登录但显示名空白时不能再落到「未登录」）。</summary>
    private static void CheckProfileName()
    {
        ChecksContext.Equal("账号页: 显示名优先于邮箱", "Alice", AccountDisplayText.ProfileName(" Alice ", "a@b.c", "未登录"));
        ChecksContext.Equal("账号页: 显示名空白 → 用邮箱", "a@b.c", AccountDisplayText.ProfileName("   ", "a@b.c", "未登录"));
        ChecksContext.Equal("账号页: 显示名为空 → 用邮箱", "a@b.c", AccountDisplayText.ProfileName(null, "  a@b.c  ", "未登录"));
        ChecksContext.Equal("账号页: 都没有 → 兜底文案", "未登录", AccountDisplayText.ProfileName(null, null, "未登录"));
        ChecksContext.Equal("账号页: 两边都空白 → 兜底文案", "未登录", AccountDisplayText.ProfileName(" ", string.Empty, "未登录"));
    }

    /// <summary>显示名与邮箱相同（忽视大小写与首尾空白）才算是「同一个人」。</summary>
    private static void CheckSameIdentity()
    {
        ChecksContext.Check(
            "账号页: 邮箱即显示名 → 判定相同",
            AccountDisplayText.SameIdentity("1714769724@qq.com", "1714769724@qq.com"));
        ChecksContext.Check(
            "账号页: 判定忽略大小写与空白",
            AccountDisplayText.SameIdentity(" A@B.C ", "a@b.c"));
        ChecksContext.Check(
            "账号页: 显示名与邮箱不同 → 判定不同",
            !AccountDisplayText.SameIdentity("Player", "a@b.c"));
        ChecksContext.Check(
            "账号页: 任一侧为空 → 判定不同",
            !AccountDisplayText.SameIdentity(null, "a@b.c") && !AccountDisplayText.SameIdentity("a@b.c", "  "));
    }

    /// <summary>
    /// 身份卡状态行新增的两条文案键必须在 zh-CN / en-US 两侧都存在 ——
    /// 漏一侧界面上就是空白（生成器不参与 build.cmd，这类漏键不会有别的提示）。
    /// </summary>
    private static void CheckStatusSessionKeys()
    {
        string[] languages = ["zh-CN", "en-US"];
        string[] keys = ["account.statusSessionAuto", "account.statusSessionOnce"];

        foreach (var language in languages)
        {
            var path = RepoPath(Path.Combine("tools", "i18n", language, "account.json"));
            if (!File.Exists(path))
            {
                ChecksContext.Check("账号页: 文案表存在", false, path);
                continue;
            }

            var lines = File.ReadAllLines(path);
            foreach (var key in keys)
            {
                var needle = "\"" + key + "\":";
                var found = lines.Any(line => line.TrimStart().StartsWith(needle, StringComparison.Ordinal));
                ChecksContext.Check("账号页: " + language + " 有键 " + key, found, path);
            }
        }
    }

    /// <summary>从检查程序所在目录往上找仓根（含 tools 的那个目录）。</summary>
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
