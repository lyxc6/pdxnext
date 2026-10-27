namespace PdxLauncher.Core.Configuration;

/// <summary>本启动器自己的设置（与 P 社启动器完全隔离，绝不写它的目录）。</summary>
public sealed class AppSettings
{
    /// <summary>是否允许联网刷新主题素材（默认开；关掉后只用安装目录素材）。</summary>
    public bool OnlineThemeRefreshEnabled { get; set; } = true;

    /// <summary>
    /// 是否允许联网补全 DLC 简介（默认开；CK3/Stellaris 的 .dlc 里没有简介，需向 Steam 商店接口查询）。
    /// 关掉后只用本地磁盘缓存，离线时也不会卡在请求上。
    /// </summary>
    public bool OnlineDlcDescriptionEnabled { get; set; } = true;

    private int _dlcDescriptionCacheDays = 7;

    /// <summary>
    /// DLC 简介缓存有效期（1–365，默认 7 天）：超过这个天数再打开 DLC 页会重新联网刷新。
    /// 商店明确说「没有简介」的条目另算，最多只看 1 天。
    /// </summary>
    public int DlcDescriptionCacheDays
    {
        get => _dlcDescriptionCacheDays;
        set => _dlcDescriptionCacheDays = value < 1 ? 1 : (value > 365 ? 365 : value);
    }

    /// <summary>
    /// 是否允许联网获取官方启动器的公告卡（默认开；关掉后只读本地缓存，绝不发起请求）。
    /// 公告是装饰性内容，失败不影响主页其它区域。
    /// </summary>
    public bool NewsFeedOnlineEnabled { get; set; } = true;

    /// <summary>
    /// 界面语言，取值见 <see cref="UiLanguages"/>；null 表示跟随系统。
    /// 同时决定 DLC 简介向 Steam 商店请求哪种语言（见 <see cref="Online.SteamStoreLanguage"/>）。
    /// 存字符串而不是枚举：未知值不能让整份设置反序列化失败。
    /// </summary>
    public string? UiLanguage { get; set; }

    /// <summary>
    /// 播放集页面的显示方式：<see cref="PlaySetPageDisplayModes.OfficialLauncher"/>（默认）或
    /// <see cref="PlaySetPageDisplayModes.Placeholder"/>；null 视为原版启动器布局。
    /// 存字符串而不是枚举：未知枚举值会让整份设置反序列化失败并回退默认值。
    /// </summary>
    public string? PlaySetPageDisplayMode { get; set; }

    /// <summary>
    /// 网络代理模式，取值见 <see cref="ProxyModes"/>；null 视为「旧配置」——
    /// 填了 <see cref="ProxyUrl"/> 就按手动配置，否则直连。
    /// 不给默认值：给了默认值会把旧文件里已配好的代理静默改成直连。
    /// </summary>
    public string? ProxyMode { get; set; }

    /// <summary>手动配置模式下的代理地址，如 http://127.0.0.1:7890；其它模式忽略。</summary>
    public string? ProxyUrl { get; set; }

    /// <summary>是否写 dlc_signature（默认关：CK3 的官方值不可复现，写了有被判篡改的风险）。</summary>
    public bool WriteDlcSignature { get; set; }

    /// <summary>窗口宽度（逻辑像素），0 表示用默认值。</summary>
    public double WindowWidth { get; set; }

    /// <summary>窗口高度（逻辑像素），0 表示用默认值。</summary>
    public double WindowHeight { get; set; }

    /// <summary>
    /// 是否允许联网补全 mod 缩略图（默认开；关掉后只用本地图片与已有缓存，绝不发起请求）。
    /// </summary>
    public bool OnlineModThumbnailEnabled { get; set; } = true;

    private int _modThumbnailCacheDays = 30;

    /// <summary>
    /// mod 缩略图缓存保留天数（1–3650，默认 30）：超过这个天数的图片会在下次清理时删除。
    /// </summary>
    public int ModThumbnailCacheDays
    {
        get => _modThumbnailCacheDays;
        set => _modThumbnailCacheDays = value < 1 ? 1 : (value > 3650 ? 3650 : value);
    }

    /// <summary>
    /// 窗口置顶（默认关）：开启后启动器一直停在其窗口之上，点其它程序不会把它盖住。
    /// 只由应用内图钉 / 设置页开关 / Ctrl+Shift+T 修改，不要在外部用 SetWindowPos 钉窗口。
    /// </summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>
    /// 整窗背景图的显示方式，取值见 <see cref="BackgroundDisplayModes"/>；null 视为「覆盖」。
    /// 与播放集显示方式一样存字符串：未知值不能让它反序列化失败。
    /// </summary>
    public string? BackgroundDisplayMode { get; set; }

    /// <summary>
    /// Mod 缩略图的尺寸，取值见 <see cref="ModThumbnailSizes"/>；null 视为「中」（48 像素）。
    /// 改完后 Mod 库与播放集页面在下次加载时应用。
    /// </summary>
    public string? ModThumbnailSize { get; set; }

    /// <summary>
    /// Paradox 账号是否「保持登录」（默认关）：勾了才向服务端要 refresh token 并落盘，
    /// 不勾时只留 session token，过期后必须重新输密码。与官方启动器的行为一致。
    /// </summary>
    public bool PdxRememberMe { get; set; }

    /// <summary>
    /// 上次选中的游戏 id（如 <c>crusader_kings_3</c>）。下次启动优先恢复它；
    /// 游戏已经不在了就回退到默认规则（见 <see cref="GameSelection"/>）。
    /// null 表示从没选过（旧版设置文件）。
    /// </summary>
    public string? LastGameId { get; set; }

    /// <summary>
    /// 上次选中游戏的安装根目录。同一个游戏装了多份时用它消歧；
    /// 与 <see cref="LastGameId"/> 一起命中才算精确恢复。
    /// </summary>
    public string? LastGameInstallRoot { get; set; }

    /// <summary>
    /// 设置页上次停留的分节（0 起，对应左栏 6 项）；null 或越界时落到第一节。
    /// 页面重开（含冷启动）会回到这一节。
    /// </summary>
    public int? LastSettingsSection { get; set; }

    /// <summary>
    /// 主窗口上次的位置（物理像素）。-1 表示还没记过，按系统默认居中。
    /// 与 <see cref="WindowWidth"/>/<see cref="WindowHeight"/> 一起在关闭时写入。
    /// </summary>
    public int WindowX { get; set; } = -1;

    /// <summary>主窗口上次的位置（物理像素）。-1 表示还没记过。</summary>
    public int WindowY { get; set; } = -1;
}
