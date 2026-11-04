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
    /// 是否启用「Steam 工坊直连订阅」（默认关，实验性）：开启后详情页会用 Steamworks 原生接口
    /// <c>ISteamUGC::SubscribeItem/UnsubscribeItem</c> 直接订阅，而不是打开浏览器。
    /// <para>
    /// 默认为关的原因：这需要以某个已发行游戏的 appid 调 <c>SteamAPI_InitFlat</c>，
    /// 属于借用游戏的 Steam 凭据，只在用户自己明确打开时才会发生。
    /// </para>
    /// </summary>
    public bool SteamWorkshopSubscribeEnabled { get; set; }

    /// <summary>
    /// 是否允许通过 Steamworks 读取工坊条目的真实状态（默认开）：订阅 / 已安装 / 需要更新 /
    /// 下载中 / 在 Steam 里被本地禁用。Mod 库页与工坊详情页据此显示徽标。
    /// <para>
    /// 只读，不做任何写操作（订阅、触发下载、本地禁用仍受 <see cref="SteamWorkshopSubscribeEnabled"/> 控制）。
    /// 每次查询都会拉起一个短命 <c>--steam-host</c> 子进程，代价与现有的 DLC 授权探测同级：
    /// Steam 会在这几秒里把本进程登记成「游戏中」，进程退出即撤销。
    /// </para>
    /// </summary>
    public bool SteamWorkshopStateEnabled { get; set; } = true;

    /// <summary>
    /// 手动指定的 <c>steam_api64.dll</c> 路径；null/空表示自动查找（程序目录 → 仓库 sdk/ → 游戏目录）。
    /// 本启动器不分发 Valve 的二进制，所以要么用户手填，要么靠上面几处已存在的副本。
    /// </summary>
    public string? SteamApiDllPath { get; set; }

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
    /// 性能与日志页上次停留的分节（0 起，对应左栏 5 项）；null 或越界时落到概览。
    /// 与 <see cref="LastSettingsSection"/> 分开记，两个页面互不影响。
    /// </summary>
    public int? LastDiagnosticsSection { get; set; }

    /// <summary>
    /// 性能与日志页的日志尾部是否自动换行。默认关（长行用横向滚动条），
    /// 打开后长行折行显示；只影响观感，不改日志内容。
    /// </summary>
    public bool DiagnosticsLogWrap { get; set; }

    /// <summary>启动后自动检查更新（默认开）。每机器安装仍会弹 UAC 才能升级。</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>上次成功读取更新清单的 UTC 时间；自动检查用它做 24 小时节流。</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>用户在本次版本上选择「稍后」时记下的版本号，自动检查不再重复提示。</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>
    /// 主窗口上次的位置（物理像素）。-1 表示还没记过，按系统默认居中。
    /// 与 <see cref="WindowWidth"/>/<see cref="WindowHeight"/> 一起在关闭时写入。
    /// </summary>
    public int WindowX { get; set; } = -1;

    /// <summary>主窗口上次的位置（物理像素）。-1 表示还没记过。</summary>
    public int WindowY { get; set; } = -1;

    /// <summary>
    /// 是否启用翻译（默认开）：开启后工坊列表、详情页简介与更新日志、评论区、
    /// 本地 Mod 库都会在原文之外附带译文。关闭后完全不发起翻译请求。
    /// </summary>
    public bool TranslateEnabled { get; set; } = true;

    /// <summary>
    /// 本地翻译模型是否常驻内存（默认常驻）。
    /// <para>
    /// 开着时模型加载一次就一直留在内存里，翻译随点随出；关掉后空闲 5 分钟卸载，
    /// 把约 1.3 GB 还给系统，下次翻译再重新加载（第一次会慢几秒）。
    /// </para>
    /// </summary>
    public bool KeepTranslationModelLoaded { get; set; } = true;

    /// <summary>
    /// 翻译引擎的选择方式，取值见 <see cref="TranslationEngineModes"/>；
    /// null/未知值视为 <see cref="TranslationEngineModes.Auto"/>（优先自填 API，失败回落到内置免费服务）。
    /// </summary>
    public string? TranslateEngineMode { get; set; }

    /// <summary>
    /// 用户自填的 OpenAI 兼容 API 根地址（如 https://api.openai.com/v1）；
    /// null 表示未配置。主机名需在白名单授权后才允许访问。
    /// </summary>
    public string? TranslateBaseUrl { get; set; }

    /// <summary>
    /// 自填 API 的密钥；null 表示未配置。会以明文形式存在本地 settings.json 里，
    /// 因此设置页用密码框呈现，日志里绝不打印。
    /// </summary>
    public string? TranslateApiKey { get; set; }

    /// <summary>自填 API 使用的模型名；null/空按 <see cref="TranslationModels.Default"/> 处理。</summary>
    public string? TranslateModel { get; set; }

    /// <summary>
    /// 微软 Azure Translator 的订阅密钥；null 表示未配置。
    /// 会以明文形式存在本地 settings.json 里，因此设置页用密码框呈现，日志里绝不打印。
    /// </summary>
    public string? TranslateMicrosoftKey { get; set; }

    /// <summary>
    /// 微软 Azure Translator 资源所在的区域（如 eastasia、global）；null 表示未配置。
    /// </summary>
    public string? TranslateMicrosoftRegion { get; set; }

    /// <summary>
    /// 谷歌云端翻译（Cloud Translation）的 API Key；null 表示未配置。
    /// 会以明文形式存在本地 settings.json 里，因此设置页用密码框呈现，日志里绝不打印。
    /// </summary>
    public string? TranslateGoogleKey { get; set; }

    /// <summary>
    /// 译文的目标语言；null/空表示「跟随界面语言」。取值见 <see cref="UiLanguages"/>。
    /// </summary>
    public string? TranslateTargetLanguage { get; set; }

    /// <summary>是否翻译 mod 名称（工坊列表行、本地 Mod 库行）；默认开。</summary>
    public bool TranslateNamesEnabled { get; set; } = true;

    /// <summary>是否翻译 mod 简介（详情页正文）；默认开。</summary>
    public bool TranslateDescriptionEnabled { get; set; } = true;

    /// <summary>是否翻译 mod 更新日志（详情页）；默认开。</summary>
    public bool TranslateChangelogEnabled { get; set; } = true;

    /// <summary>是否翻译评论区正文；默认开。</summary>
    public bool TranslateCommentsEnabled { get; set; } = true;

    private int _translateCacheDays = 30;

    /// <summary>
    /// 译文缓存保留天数（1–365，默认 30）：超过这个天数的译文会重新向引擎请求。
    /// </summary>
    public int TranslateCacheDays
    {
        get => _translateCacheDays;
        set => _translateCacheDays = value < 1 ? 1 : (value > 365 ? 365 : value);
    }

    /// <summary>
    /// 创意工坊搜索框的最近提交词（最新的在前，最多 8 条，大小写不敏感去重）。
    /// 空列表表示还没提交过搜索；旧设置文件里没有这个键，读到的是默认空列表，
    /// 但文档里若显式写了 null，调用点要按 <c>?? []</c> 兜底。
    /// </summary>
    public List<string> WorkshopSearchHistory { get; set; } = [];
}
