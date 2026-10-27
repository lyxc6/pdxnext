namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// Steam 创意工坊地址拼装自检：创意工坊 id 的来源优先级（steamId → mod/ugc_&lt;id&gt;.mod）、
/// 非数字与空值不产出地址、两种地址前缀正确。
/// 不联网、不启动任何进程。
/// </summary>
public static class SteamWorkshopChecks
{
    private const string BrowserPrefix = "https://steamcommunity.com/sharedfiles/filedetails/?id=";
    private const string SteamPrefix = "steam://url/CommunityFilePage/";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckSteamIdPreferred();
        CheckRegistryFallback();
        CheckNoWorkshopId();
        CheckAddresses();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckSteamIdPreferred()
    {
        ChecksContext.Equal(
            "创意工坊 id：优先取 steamId",
            "2216623537",
            SteamWorkshop.GetWorkshopId("2216623537", "mod/ugc_999.mod"));

        ChecksContext.Equal(
            "创意工坊 id：steamId 前后空白被裁掉",
            "123",
            SteamWorkshop.GetWorkshopId("  123  ", null));

        ChecksContext.Equal(
            "创意工坊 id：steamId 不是数字时回退注册 ID",
            "555",
            SteamWorkshop.GetWorkshopId("abc", "mod/ugc_555.mod"));
    }

    private static void CheckRegistryFallback()
    {
        // CK3 真机 387 行里 382 行的注册 ID 就是这种形状。
        ChecksContext.Equal(
            "创意工坊 id：从 mod/ugc_<id>.mod 解析",
            "2216623537",
            SteamWorkshop.GetWorkshopId(null, "mod/ugc_2216623537.mod"));

        ChecksContext.Equal(
            "创意工坊 id：注册 ID 无 ugc_ 前缀时为空",
            null,
            SteamWorkshop.GetWorkshopId(null, "mod/legen...mod"));

        ChecksContext.Equal(
            "创意工坊 id：ugc_ 后面没有数字时为空",
            null,
            SteamWorkshop.GetWorkshopId(null, "mod/ugc_.mod"));

        ChecksContext.Equal(
            "创意工坊 id：只取 ugc_ 之后的连续数字",
            "778899",
            SteamWorkshop.GetWorkshopId(null, "mod/ugc_778899_extra.mod"));
    }

    private static void CheckNoWorkshopId()
    {
        ChecksContext.Equal("创意工坊 id：两个来源都为空", null, SteamWorkshop.GetWorkshopId(null, null));
        ChecksContext.Equal("创意工坊 id：空串按缺失处理", null, SteamWorkshop.GetWorkshopId(string.Empty, string.Empty));
        ChecksContext.Equal("创意工坊 id：空白按缺失处理", null, SteamWorkshop.GetWorkshopId("   ", "   "));

        // 全角数字不是创意工坊 id：拼进地址只会打开 404。
        ChecksContext.Equal("创意工坊 id：全角数字不算数字", null, SteamWorkshop.GetWorkshopId("２２２", null));
    }

    private static void CheckAddresses()
    {
        ChecksContext.Equal("浏览器地址前缀", BrowserPrefix + "123", SteamWorkshop.GetBrowserAddress("123"));
        ChecksContext.Equal("Steam 地址前缀", SteamPrefix + "123", SteamWorkshop.GetSteamAddress("123"));
        ChecksContext.Equal("浏览器地址：id 为空时不产出", null, SteamWorkshop.GetBrowserAddress(null));
        ChecksContext.Equal("Steam 地址：id 为空时不产出", null, SteamWorkshop.GetSteamAddress(string.Empty));
    }
}
