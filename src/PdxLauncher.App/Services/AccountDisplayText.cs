namespace PdxLauncher.App.Services;

/// <summary>
/// 身份卡上「名字 / 首字母 / 是不是同一个人」的取值规则。纯 C#，不引用任何 UI 类型，可脱离 UI 自检。
/// </summary>
/// <remarks>
/// 拆出来的原因是一次真机观感问题：Paradox 返回的 <c>DisplayName</c> 往往就是邮箱本身，
/// 于是头像取了「1714769724@qq.com」的首字符，界面上出现一个大号的「1」，看着像步骤序号；
/// 同时状态行又写成「已登录：1714769724@qq.com（1714769724@qq.com）」，同一个邮箱重复两遍。
/// 两条都是「取值规则」的问题而不是排版问题，所以规则集中在这里，由自检钉住，
/// 界面只负责消费结果。
/// </remarks>
public static class AccountDisplayText
{
    /// <summary>
    /// 头像首字母：按 显示名 → 邮箱 的顺序，各自只看 <b>本地段</b>（<c>@</c> 之前）里的第一个字母；
    /// 都找不到就返回空串。
    /// </summary>
    /// <param name="displayName">Paradox 返回的显示名（经常就是邮箱）。</param>
    /// <param name="email">邮箱；未登录时是用户正在输入的邮箱。</param>
    /// <remarks>
    /// <para>
    /// 跳过数字与符号是有意的：纯数字的本地段（如 <c>1714769724@qq.com</c>）取首字符会得到「1」，
    /// 落到头像上就成了一个莫名其妙的序号。
    /// </para>
    /// <para>
    /// 显示名要先切掉 <c>@</c> 之后的部分：Paradox 的显示名常常就是邮箱，
    /// 直接找「第一个字母」会落到域名里的 <c>qq</c>，圆底上出现一个「Q」——那和这个人没有关系，
    /// 还容易被当成搜索按钮。返回空串时 PersonPicture 自己会画一个通用人像，比一个假的字母更准确。
    /// </para>
    /// </remarks>
    public static string Initials(string? displayName, string? email)
    {
        return FirstLetter(LocalPart(displayName))
            ?? FirstLetter(LocalPart(email))
            ?? string.Empty;
    }

    /// <summary>
    /// 身份卡上的名字：显示名 → 邮箱 → <paramref name="fallback"/>。
    /// </summary>
    /// <param name="displayName">显示名；空白视为没有。</param>
    /// <param name="email">邮箱；空白视为没有。</param>
    /// <param name="fallback">都没有时显示的文案（一般是「未登录」）。</param>
    /// <remarks>
    /// 已登录但显示名为空时曾经直接落到 <paramref name="fallback"/>，界面上就是「已登录却写着未登录」。
    /// </remarks>
    public static string ProfileName(string? displayName, string? email, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return string.IsNullOrWhiteSpace(email) ? fallback : email.Trim();
    }

    /// <summary>显示名是不是就是邮箱本身（忽略大小写与首尾空白）。</summary>
    /// <param name="displayName">显示名。</param>
    /// <param name="email">邮箱。</param>
    /// <remarks>两边都有值且相同才算；任意一边为空都返回 <c>false</c>。</remarks>
    public static bool SameIdentity(string? displayName, string? email)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return string.Equals(displayName.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>取第一个字母并大写；没有字母（含空串）返回 <c>null</c>。</summary>
    private static string? FirstLetter(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        foreach (var ch in value)
        {
            if (char.IsLetter(ch))
            {
                return char.ToUpperInvariant(ch).ToString();
            }
        }

        return null;
    }

    /// <summary>邮箱的本地段（<c>@</c> 之前）；没有 <c>@</c> 时把整串当本地段。</summary>
    private static string? LocalPart(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        return at < 0 ? trimmed : trimmed[..at];
    }
}
