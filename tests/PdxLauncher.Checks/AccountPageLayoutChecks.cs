namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 账号页版式自检：把「看起来不对劲」的几处结论钉成源码断言。
/// <para>
/// 这条需求来自真机截图，不是编译错误也不是运行时异常 —— 重排一次页面就可能悄悄退回去：
/// InfoBar 又多写一个 Title（变成两行、中间空一大截）、开关又被塞回「开 / 关」两个字、
/// 身份卡又换回另一套画刷（和下面的卡片明显不是一套）、颜色又被写死（Light 主题下直接糊掉）。
/// 所以这里读 XAML 源文本断言，而不是在运行时截图比对。
/// </para>
/// 同类的先例见 <see cref="ConfirmOverlayChecks"/>（它钉的是 ConfirmOverlay 的按钮顺序）。
/// </summary>
public static class AccountPageLayoutChecks
{
    /// <summary>页面必须保留的 AutomationId：UI 自动化脚本与 HANDOFF 的走查都靠它们。</summary>
    private static readonly string[] RequiredAutomationIds =
    [
        "imgAccountSignInAvatar",
        "cardAccountSignIn",
        "imgAccountAvatar",
        "txtAccountProfileName",
        "txtAccountStatus",
        "btnAccountSignOut",
        "infoAccountStatus",
        "txtAccountStatusBar",
        "txtAccountEmail",
        "txtAccountPassword",
        "swAccountRemember",
        "btnAccountSignIn",
        "grpAccountSignedIn",
        "swAccountRememberSignedIn",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "AccountPage.xaml");

        if (!ChecksContext.Check("账号页: AccountPage.xaml 存在", File.Exists(xamlPath), xamlPath))
        {
            return ChecksContext.Failures - before;
        }

        var xaml = File.ReadAllText(xamlPath);

        CheckStatusBarIsSingleLine(xaml);
        CheckSignInCardIsSingleStep(xaml);
        CheckTogglesHaveNoOnOffText(xaml);
        CheckContentColumnStretches(xaml);
        CheckCardsShareBrushes(xaml);
        CheckNoHardCodedColors(xaml);
        CheckAutomationIds(xaml);

        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 未登录时不该再点一次才看到表单：整页就是一张登录卡，输入框与主按钮都在卡里。
    /// 这条需求来自真机截图（身份卡上的「登录」只负责展开下面的 Expander，点两次才看到输入框）。
    /// </summary>
    private static void CheckSignInCardIsSingleStep(string xaml)
    {
        ChecksContext.Check(
            "账号页: 未登录不再用 Expander 藏表单",
            !xaml.Contains("<Expander", StringComparison.Ordinal));

        var start = xaml.IndexOf("AutomationId=\"cardAccountSignIn\"", StringComparison.Ordinal);
        if (!ChecksContext.Check("账号页: 登录卡仍在", start >= 0))
        {
            return;
        }

        var end = xaml.IndexOf("</Border>", start, StringComparison.Ordinal);
        if (!ChecksContext.Check("账号页: 登录卡闭合标签", end > start))
        {
            return;
        }

        var card = xaml[start..end];
        ChecksContext.Check(
            "账号页: 邮箱框在登录卡里",
            card.Contains("AutomationId=\"txtAccountEmail\"", StringComparison.Ordinal),
            "输入框必须在首屏可见的卡片内");
        ChecksContext.Check(
            "账号页: 密码框在登录卡里",
            card.Contains("AutomationId=\"txtAccountPassword\"", StringComparison.Ordinal),
            "输入框必须在首屏可见的卡片内");
        ChecksContext.Check(
            "账号页: 登录按钮在登录卡里",
            card.Contains("AutomationId=\"btnAccountSignIn\"", StringComparison.Ordinal),
            "主操作不能只留在身份卡上");
        ChecksContext.Check(
            "账号页: 登录卡是两列（说明 + 表单）",
            card.Contains("<Grid.ColumnDefinitions>", StringComparison.Ordinal)
                && card.Contains("Width=\"200\"", StringComparison.Ordinal),
            "左列说明、右列表单");
    }
    /// <summary>
    /// 状态条只有一行文字。
    /// InfoBar 同时有 Title 与 Content 时是「标题行 + 内容行」的布局，两行之间空一大截；
    /// 而且「登录状态」这个标题本身不携带信息。
    /// </summary>
    private static void CheckStatusBarIsSingleLine(string xaml)
    {
        var start = xaml.IndexOf("x:Name=\"infoAccountStatus\"", StringComparison.Ordinal);
        if (!ChecksContext.Check("账号页: 状态条仍在", start >= 0))
        {
            return;
        }

        var end = xaml.IndexOf("</InfoBar>", start, StringComparison.Ordinal);
        if (!ChecksContext.Check("账号页: 状态条闭合标签", end > start))
        {
            return;
        }

        var block = xaml[start..end];
        ChecksContext.Check(
            "账号页: 状态条不再设置 Title（单行显示）",
            !block.Contains("Title=", StringComparison.Ordinal),
            "InfoBar 同时有 Title 与 Content 时会变成两行");
    }

    /// <summary>
    /// 开关不带「开 / 关」文字：卡片标题已经写了这是什么开关。
    /// <para>
    /// 注意「不写 OnContent」并不等于「不显示文字」——不写时控件用平台默认文案，
    /// 中文系统上就是「开 / 关」，和显式写上去一模一样（第一版就是这么翻车的：
    /// 删掉 OnContent 之后截图里照样有「开」）。必须显式赋空串，三处开关都要。
    /// </para>
    /// </summary>
    private static void CheckTogglesHaveNoOnOffText(string xaml)
    {
        var nonEmptyOn = Regex.Match(xaml, "OnContent=\"[^\"]+");
        var nonEmptyOff = Regex.Match(xaml, "OffContent=\"[^\"]+");
        ChecksContext.Check(
            "账号页: 没有非空的 OnContent",
            !nonEmptyOn.Success,
            nonEmptyOn.Success ? nonEmptyOn.Value : null);
        ChecksContext.Check(
            "账号页: 没有非空的 OffContent",
            !nonEmptyOff.Success,
            nonEmptyOff.Success ? nonEmptyOff.Value : null);

        var switches = Regex.Matches(xaml, "<ToggleSwitch\\b").Count;
        ChecksContext.Equal("账号页: 页面上有 2 个开关", 2, switches);
        ChecksContext.Equal("账号页: 每个开关都显式赋了空 OnContent", switches, Regex.Matches(xaml, "OnContent=\"\"").Count);
        ChecksContext.Equal("账号页: 每个开关都显式赋了空 OffContent", switches, Regex.Matches(xaml, "OffContent=\"\"").Count);
    }

    /// <summary>身份卡与 SettingsCard 用同一套卡片画刷，页面上不再有两种「卡片」。</summary>
    /// <summary>
    /// 内容列要横向撑开。
    /// StackPanel 在纵向布局里是「按内容宽度收缩」的：一旦写成 HorizontalAlignment="Left"，
    /// MaxWidth 就只剩上限作用，卡片会被内容挤成一条窄带，右侧留出大片空白。
    /// 必须用 Stretch，让 MaxWidth 变成实际宽度。
    /// </summary>
    private static void CheckContentColumnStretches(string xaml)
    {
        var match = Regex.Match(xaml, "<StackPanel\\b[^>]*Padding=\"24\"[^>]*>");

        ChecksContext.Check("账号页: 内容列仍是 MaxWidth 880 的容器", match.Success);
        if (!match.Success)
        {
            return;
        }

        ChecksContext.Check(
            "账号页: 内容列横向撑开（HorizontalAlignment=Stretch）",
            match.Value.Contains("HorizontalAlignment=\"Stretch\"", StringComparison.Ordinal),
            match.Value);
    }

    private static void CheckCardsShareBrushes(string xaml)
    {
        ChecksContext.Check(
            "账号页: 身份卡用 PdxCardBackgroundBrush",
            xaml.Contains("{ThemeResource PdxCardBackgroundBrush}", StringComparison.Ordinal));
        ChecksContext.Check(
            "账号页: 身份卡用 PdxCardStrokeBrush",
            xaml.Contains("{ThemeResource PdxCardStrokeBrush}", StringComparison.Ordinal));
    }

    /// <summary>不写死颜色：写字面量色值在 Light / HighContrast 下必糊，仓库统一用主题画刷。</summary>
    private static void CheckNoHardCodedColors(string xaml)
    {
        var literal = Regex.Match(xaml, "#[0-9A-Fa-f]{3,8}\\b");
        ChecksContext.Check(
            "账号页: 没有硬编码颜色",
            !literal.Success,
            literal.Success ? "发现 " + literal.Value : null);
    }

    /// <summary>交互控件的 AutomationId 一个都不能少（改版容易顺手删掉）。</summary>
    private static void CheckAutomationIds(string xaml)
    {
        foreach (var id in RequiredAutomationIds)
        {
            ChecksContext.Check(
                "账号页: 保留 AutomationId " + id,
                xaml.Contains("AutomationId=\"" + id + "\"", StringComparison.Ordinal));
        }
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
