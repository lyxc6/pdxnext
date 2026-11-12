namespace PdxLauncher.Core.Steam;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 启动器与「Steam 宿主子进程」之间的命令行 / 结果契约。
/// <para>
/// 为什么要有宿主子进程：只要**任何**进程以游戏的 appid 调 <c>SteamAPI_Init</c>，Steam 客户端
/// 就会把它登记成该 app 的 game process（<c>Game process added : AppID …</c>），而
/// <c>SteamAPI_Shutdown</c> **不会**撤销这条登记 —— 只有进程退出才会
/// （<c>Game process removed</c>）。登记状态下的进程会被 Steam 的「停止 / 退出游戏」直接终止。
/// 所以原生 Steamworks 调用一律放进短命子进程：探测约 1 秒、订阅最多二十几秒，
/// 主启动器进程永不登记，也就不再被 Steam 当成游戏杀掉。
/// </para>
/// <para>
/// 宿主就是启动器自己（<c>PdxLauncher.App.exe --steam-host …</c>，见
/// <c>src/PdxLauncher.App/Program.cs</c>）：WinUI 在 <c>Application.Start</c> 之前拦截该参数，
/// 这样不新增部署物，publish 清单也不会漂移。
/// </para>
/// </summary>
public static class SteamHostProtocol
{
    /// <summary>识别宿主模式的参数（必须是第一个参数）。</summary>
    public const string VerbArgument = "--steam-host";

    /// <summary>只读 DLC 授权探测。</summary>
    public const string ProbeVerb = "probe";

    /// <summary>
    /// 读取当前账号在某款游戏下的成就列表（含解锁状态、解锁时间、全球解锁率）。
    /// <para>
    /// 成就元数据与解锁状态都来自 <c>ISteamUserStats</c>：必须先 <c>RequestUserStats</c>，
    /// 再等 <c>UserStatsReceived_t</c>（callback id 1101）才拿得到当前账号的数据，
    /// 所以这个动词比探测要慢，但通常也在几百毫秒内。
    /// </para>
    /// </summary>
    public const string AchievementsVerb = "achievements";

    /// <summary>
    /// 探测某个 <c>steam_api64.dll</c> 能不能用于读成就（导出表里有没有 UserStats 那套）。
    /// <para>
    /// 单独做成动词的原因：Load 会执行 DllMain，loader 形态的 DLL 甚至可能直接崩，
    /// 所以探测必须发生在这个子进程里；主进程只拿结论，不碰原生库。
    /// </para>
    /// </summary>
    public const string ApiCheckVerb = "api-check";

    /// <summary>读某个工坊条目的订阅状态。</summary>
    public const string WorkshopStateVerb = "workshop-state";

    /// <summary>订阅某个工坊条目。</summary>
    public const string WorkshopSubscribeVerb = "workshop-subscribe";

    /// <summary>取消订阅某个工坊条目。</summary>
    public const string WorkshopUnsubscribeVerb = "workshop-unsubscribe";

    /// <summary>创建 / 更新一个工坊条目并上传内容。</summary>
    public const string WorkshopPublishVerb = "workshop-publish";

    /// <summary>一次拉取当前账号在本游戏下订阅的全部工坊条目（含安装状态）。</summary>
    public const string WorkshopSubscribedVerb = "workshop-subscribed";

    /// <summary>对一批工坊条目做同一个动作（下载 / 本地禁用 / 解除本地禁用 / 只读状态）。</summary>
    public const string WorkshopBatchVerb = "workshop-batch";

    /// <summary>
    /// 列出当前账号在本游戏下**自己发布**的工坊条目 id。
    /// <para>
    /// 走 <c>ISteamRemoteStorage::EnumerateUserPublishedFiles</c>：吃本机登录态、不收 API key，
    /// 未公开（仅好友 / 私密 / 未列出）的条目同样在列表里 —— 这正是 Web API 的
    /// <c>GetPublishedFileDetails</c> 给匿名调用方回 <c>EResult 9 (FileNotFound)</c> 时唯一的补救途径。
    /// 注意它只能列出**自己**的条目，与「订阅列表」是两回事。
    /// </para>
    /// </summary>
    public const string WorkshopMineVerb = "workshop-mine";

    /// <summary>按 id 读**自己**某个工坊条目的详情（含真实可见性）；配合 <c>--fileid</c> 使用。</summary>
    public const string WorkshopMineDetailVerb = "workshop-mine-detail";

    /// <summary>
    /// 常驻宿主：进程起一次、<c>SteamAPI_Init</c> 只做一次，之后从 stdin 逐行收命令、往 stdout 逐行回结果。
    /// <para>
    /// 与一次性动词共用 <c>--steam-host</c> 入口和 <c>--api</c>/<c>--appid</c> 参数。
    /// <b>产品路径已不再使用它</b>：Steam 会把调过 <c>SteamAPI_Init</c> 的进程登记成该 appid 的
    /// game process，常驻 worker 会让 Steam 在两次读取之间一直显示「游戏正在运行」。
    /// 成就读取改走一次性 <see cref="AchievementsVerb"/>；重新接线前必须先处理这个副作用。
    /// 行格式见 <see cref="SteamHostWorkerProtocol"/>。
    /// </para>
    /// </summary>
    public const string ServeVerb = "serve";

    /// <summary>结果行的前缀；宿主只往 stdout 写这一行 + 换行。</summary>
    public const string ResultPrefix = "PDXNEXT-STEAM-HOST ";

    /// <summary>发布进度行的前缀；与最终结果行分开，客户端逐行读时不会把进度当结果。</summary>
    public const string ProgressPrefix = "PDXNEXT-STEAM-HOST-PROGRESS ";

    /// <summary>宿主正常完成（结果行有效）。</summary>
    public const int ExitOk = 0;

    /// <summary>宿主失败：参数错误、原生库缺失、入口点缺失等（结果行仍可能带错误键）。</summary>
    public const int ExitFailure = 1;

    /// <summary>所有已知动词。</summary>
    public static IReadOnlyList<string> Verbs { get; } = new[]
    {
        ProbeVerb,
        AchievementsVerb,
        ApiCheckVerb,
        WorkshopStateVerb,
        WorkshopSubscribeVerb,
        WorkshopUnsubscribeVerb,
        WorkshopPublishVerb,
        WorkshopSubscribedVerb,
        WorkshopBatchVerb,
        WorkshopMineVerb,
        WorkshopMineDetailVerb,
        ServeVerb,
    };

    /// <summary>共享的 JSON 设置：camelCase、忽略 null、不回填只有 getter 的派生属性。</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true,
    };

    /// <summary>参数表里到处都要先看一眼的命令行参数。</summary>
    public static bool IsHostInvocation(IReadOnlyList<string> args) =>
        args.Count > 1 && args[0] == VerbArgument;

    /// <summary>取动词；不是宿主调用或动词未知时返回 false。</summary>
    public static bool TryGetVerb(IReadOnlyList<string> args, out string verb)
    {
        verb = string.Empty;
        if (!IsHostInvocation(args))
        {
            return false;
        }

        var candidate = args[1];
        if (!Verbs.Contains(candidate))
        {
            return false;
        }

        verb = candidate;
        return true;
    }

    /// <summary>把结果对象编成宿主写到 stdout 的那一行。</summary>
    public static string Encode<T>(T payload) => ResultPrefix + JsonSerializer.Serialize(payload, JsonOptions);

    /// <summary>把发布进度编成一行；客户端逐行读 stdout 时先认这个前缀。</summary>
    public static string EncodeProgress<T>(T payload) => ProgressPrefix + JsonSerializer.Serialize(payload, JsonOptions);

    /// <summary>从一行 stdout 里解出发布进度；不是进度行或格式不对返回 false。</summary>
    public static bool TryDecodeProgress<T>(string? line, out T? payload)
        where T : class
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var index = line.IndexOf(ProgressPrefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var json = line[(index + ProgressPrefix.Length)..].Trim();
        if (json.Length == 0)
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return payload is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>从子进程 stdout 里找出结果行并反序列化；找不到或格式不对返回 false。</summary>
    public static bool TryDecode<T>(string? stdout, out T? payload)
        where T : class
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return false;
        }

        var index = stdout.IndexOf(ResultPrefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var start = index + ResultPrefix.Length;
        var end = stdout.IndexOf('\n', start);
        var json = (end < 0 ? stdout.Substring(start) : stdout.Substring(start, end - start)).Trim();
        if (json.Length == 0)
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return payload is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// 宿主执行一次授权探测后回报的结果。字段与 <see cref="SteamDllProbeResult"/> 一一对应，
/// 只是把错误键和原生细节拆成两个可独立判读的字段。
/// </summary>
/// <param name="Initialized"><c>SteamAPI_Init</c> 是否成功；成功即意味着**本进程**已被 Steam 登记。</param>
/// <param name="SteamRunning">Steam 客户端是否在运行。</param>
/// <param name="SteamId64">当前账号的 SteamID64；0 表示没取到。</param>
/// <param name="Dlcs">Steam 列出的 DLC。</param>
/// <param name="Error">失败原因键（见 <see cref="SteamProbeError"/>）；成功时为 null。</param>
/// <param name="Detail">原生错误文本。</param>
public sealed record SteamHostProbeResult(
    bool Initialized,
    bool SteamRunning,
    ulong SteamId64,
    IReadOnlyList<SteamDlcOwnership> Dlcs,
    string? Error,
    string? Detail)
{
    /// <summary>失败结果（没加载到原生库、参数不对等）。</summary>
    public static SteamHostProbeResult Fail(string error, string? detail = null) =>
        new(false, false, 0, Array.Empty<SteamDlcOwnership>(), error, detail);

    /// <summary>
    /// 宿主进程是否被 Steam 登记过。只要 <c>SteamAPI_Init</c> 成功就是 true —— 真正的
    /// 「Steam 认为游戏在运行」只由这个值决定，登记随进程退出自动消失。
    /// </summary>
    public bool WasRegistered => Initialized;
}

/// <summary>宿主执行一次工坊订阅 / 取消订阅 / 读状态后回报的结果。</summary>
/// <param name="Ok">操作是否按预期完成。</param>
/// <param name="State">读取到的安装状态（<see cref="WorkshopInstallState.Unknown"/> 表示没读到）。</param>
/// <param name="Error">失败原因（中文，直接进日志）。</param>
/// <param name="Initialized"><c>SteamAPI_Init</c> 是否成功（= 本进程被 Steam 登记过）。</param>
public sealed record SteamHostWorkshopResult(bool Ok, WorkshopInstallState? State, string? Error, bool Initialized)
{
    /// <summary>失败结果。</summary>
    public static SteamHostWorkshopResult Fail(string error) =>
        new(false, null, error, false);
}

/// <summary>发布请求；由 App 侧序列化、base64 后通过 <c>--request</c> 传给宿主。</summary>
/// <param name="AppId">游戏的 Steam AppID。</param>
/// <param name="PublishedFileId">已有条目 id；为空表示新建。</param>
/// <param name="ContentPath">要上传的本地 mod 目录。</param>
/// <param name="PreviewPath">主封面图路径；为空时不提交封面。</param>
/// <param name="Title">工坊标题（默认语言）。</param>
/// <param name="Description">工坊简介（默认语言）。</param>
/// <param name="ChangeNote">更新说明；新建时忽略。</param>
/// <param name="Tags">工坊标签。</param>
/// <param name="Visibility">可见性四档。旧调用方用 <see cref="SteamWorkshopVisibilityExtensions.Effective"/> 迁移。</param>
/// <param name="ContentDescriptors">内容描述词 id（1-5）；为空表示一个都不勾。</param>
/// <param name="PreviewPaths">附加预览图（本地文件，单张必须 &lt; 1 MB）；不含主封面。</param>
/// <param name="PreviewVideoIds">附加预览视频的 YouTube 视频 ID。</param>
/// <param name="Localizations">默认语言之外的标题/描述，按语言逐次提交。</param>
public sealed record ModPublishRequest(
    int AppId,
    ulong? PublishedFileId,
    string ContentPath,
    string? PreviewPath,
    string Title,
    string Description,
    string? ChangeNote,
    IReadOnlyList<string> Tags,
    SteamWorkshopVisibility Visibility,
    IReadOnlyList<int>? ContentDescriptors = null,
    IReadOnlyList<string>? PreviewPaths = null,
    IReadOnlyList<string>? PreviewVideoIds = null,
    IReadOnlyList<ModLocalizedText>? Localizations = null);

/// <summary>
/// 发布相关的大小 / 数量限制，与 Steam 侧约束保持一致。
/// <para>
/// 文本上限的口径是 <b>UTF-8 字节</b>，不是字符：Steamworks 头里对应的是
/// <c>k_cchPublishedDocumentTitleMax = 128 + 1</c> 与
/// <c>k_cchPublishedDocumentDescriptionMax = 8000</c> 这样的定长 <c>char</c> 缓冲
/// （见 <c>sdk/public/steam/isteamremotestorage.h</c>），按字节判只会更保守、不会误放行。
/// 一个汉字占 3 字节，所以中文能写 2666 字左右，英文能写 8000 个字母。
/// </para>
/// </summary>
public static class ModPublishLimits
{
    /// <summary>标题的字节上限（Steam 侧是 <c>char[128 + 1]</c>）。</summary>
    public const int MaxTitleBytes = 128;

    /// <summary>描述的字节上限（Steam 侧是 <c>char[8000]</c>）。</summary>
    public const int MaxDescriptionBytes = 8000;

    /// <summary>更新说明（changenote）的字节上限。</summary>
    public const int MaxChangeNoteBytes = 8000;

    /// <summary>单张预览图的字节上限：Steam 要求 &lt; 1 MB。</summary>
    public const long MaxPreviewBytes = 1024 * 1024;

    /// <summary>附加预览（图片 + 视频）的最大条数。</summary>
    public const int MaxAdditionalPreviews = 8;

    /// <summary>单次发布最多提交的语言数（含默认语言之外的附加语言）。</summary>
    public const int MaxLocalizations = 12;

    /// <summary>预览图大小是否可接受。</summary>
    /// <param name="bytes">文件字节数。</param>
    /// <param name="error">不可接受时的中文原因。</param>
    public static bool IsPreviewFileAcceptable(long bytes, out string? error)
    {
        if (bytes <= 0)
        {
            error = "预览图是空文件";
            return false;
        }

        if (bytes >= MaxPreviewBytes)
        {
            error = "预览图超过 Steam 的 1 MB 限制";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>算字符串的 UTF-8 字节数（null / 空串记 0）。</summary>
    /// <param name="value">文本。</param>
    public static int Utf8Length(string? value) =>
        string.IsNullOrEmpty(value) ? 0 : System.Text.Encoding.UTF8.GetByteCount(value);

    /// <summary>标题是否在 Steam 上限内（空标题交给调用方另判「必填」）。</summary>
    /// <param name="title">标题。</param>
    /// <param name="error">超限时的中文原因。</param>
    public static bool TryValidateTitle(string? title, out string? error) =>
        TryValidateText(title, MaxTitleBytes, "标题", out error);

    /// <summary>描述是否在 Steam 上限内（空描述合法，Steam 允许一条目没有描述）。</summary>
    /// <param name="description">描述（BBCode 原文也按原文算字节）。</param>
    /// <param name="error">超限时的中文原因。</param>
    public static bool TryValidateDescription(string? description, out string? error) =>
        TryValidateText(description, MaxDescriptionBytes, "描述", out error);

    /// <summary>更新说明是否在 Steam 上限内（空值合法，表示这次不写更新说明）。</summary>
    /// <param name="changeNote">更新说明。</param>
    /// <param name="error">超限时的中文原因。</param>
    public static bool TryValidateChangeNote(string? changeNote, out string? error) =>
        TryValidateText(changeNote, MaxChangeNoteBytes, "更新说明", out error);

    private static bool TryValidateText(string? value, int maxBytes, string label, out string? error)
    {
        var used = Utf8Length(value);
        if (used <= maxBytes)
        {
            error = null;
            return true;
        }

        error = label + "太长：" + used + " 字节，Steam 上限 " + maxBytes + " 字节（一个汉字按 3 字节算）";
        return false;
    }
}

/// <summary>发布过程中宿主逐行回报的进度。</summary>
/// <param name="Stage">阶段：creating / preparing / uploading / preview / committing。</param>
/// <param name="Percent">0-100；无法计算时为 0。</param>
/// <param name="BytesUploaded">已上传字节。</param>
/// <param name="BytesTotal">总字节；0 表示未知。</param>
public sealed record ModPublishProgress(string Stage, int Percent, ulong BytesUploaded, ulong BytesTotal);

/// <summary>发布最终结果。</summary>
/// <param name="Ok">是否成功。</param>
/// <param name="PublishedFileId">工坊条目 id；失败但已创建条目时也可能有值。</param>
/// <param name="NeedsLegalAgreement">用户是否还需要在 Steam 接受工坊法律协议。</param>
/// <param name="Error">失败原因（中文，直接进日志 / UI）。</param>
public sealed record ModPublishResult(
    bool Ok,
    ulong? PublishedFileId,
    bool NeedsLegalAgreement,
    string? Error)
{
    /// <summary>失败结果。</summary>
    public static ModPublishResult Fail(string error, ulong? publishedFileId = null, bool needsLegalAgreement = false) =>
        new(false, publishedFileId, needsLegalAgreement, error);
}

/// <summary>批量动作的种类；值即宿主命令行上的 <c>--op</c>。</summary>
public static class WorkshopBatchOps
{
    /// <summary>只读状态（不改变任何东西）。</summary>
    public const string State = "state";

    /// <summary>触发下载（Steam 侧立即排队）。</summary>
    public const string Download = "download";

    /// <summary>在 Steam 里本地禁用（订阅关系保留）。</summary>
    public const string Disable = "disable";

    /// <summary>解除本地禁用。</summary>
    public const string Enable = "enable";

    /// <summary>所有已知动作。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { State, Download, Disable, Enable };

    /// <summary>这个动作会不会改变 Steam 侧状态（决定要不要用户显式开启「写入」开关）。</summary>
    /// <param name="op">动作名。</param>
    /// <returns>只有已知的写动作为 true；<see cref="State"/>、null 与未知动作都是 false。</returns>
    public static bool IsWrite(string? op) =>
        !string.IsNullOrEmpty(op)
        && !string.Equals(op, State, StringComparison.Ordinal)
        && All.Contains(op, StringComparer.Ordinal);
}

/// <summary>订阅列表里的一条：id + 状态 + 磁盘信息。</summary>
/// <param name="PublishedFileId">工坊条目 id。</param>
/// <param name="State">该条目的安装状态（至少 <c>IsSubscribed</c> 有值）。</param>
public sealed record WorkshopSubscribedItem(ulong PublishedFileId, WorkshopInstallState State);

/// <summary>
/// 宿主一次拉取「我订阅了哪些工坊条目」的结果。
/// <para>
/// 走 <c>GetNumSubscribedItems</c> + <c>GetSubscribedItems</c> 批量拿 id，再逐条
/// <c>GetItemState</c>/<c>GetItemInstallInfo</c>/<c>GetItemDownloadInfo</c> 补齐磁盘信息。
/// 一次宿主调用顶掉原先「每行起一个子进程」的做法。
/// </para>
/// </summary>
/// <param name="Ok">整批是否成功拿到（单条失败不影响整体）。</param>
/// <param name="Items">订阅条目；<see cref="Ok"/> 为 false 时为空。</param>
/// <param name="Error">失败原因（中文，直接进日志）。</param>
/// <param name="Initialized"><c>SteamAPI_Init</c> 是否成功。</param>
/// <param name="Truncated">因 <see cref="MaxItems"/> 上限被截断时为 true。</param>
public sealed record SteamHostSubscribedResult(
    bool Ok,
    IReadOnlyList<WorkshopSubscribedItem> Items,
    string? Error,
    bool Initialized,
    bool Truncated = false)
{
    /// <summary>单次宿主动作最多回传的条目数（防止超大订阅库把一行 JSON 撑爆）。</summary>
    public const int MaxItems = 4000;

    /// <summary>失败结果。</summary>
    public static SteamHostSubscribedResult Fail(string error) =>
        new(false, Array.Empty<WorkshopSubscribedItem>(), error, false);
}

/// <summary>
/// 宿主列出「我自己在这个游戏下发布了哪些工坊条目」的结果。
/// <para>
/// 与 <see cref="SteamHostSubscribedResult"/> 的区别：那边是**订阅**（别人的条目装到本机），
/// 这边是**自己的发布**（作者视角，含未公开条目）。两者没有交集可依赖 —— 作者一般不订阅自己。
/// </para>
/// </summary>
/// <param name="Ok">是否成功拿到列表（单条无效 id 不影响整体）。</param>
/// <param name="Ids">自己的条目 id（去重、剔除 0）。</param>
/// <param name="Total">Steam 报的总条数；&lt; 0 表示没读到。</param>
/// <param name="Truncated">因翻页上限被截断时为 true。</param>
/// <param name="Error">失败原因（中文，直接进日志）。</param>
/// <param name="Initialized"><c>SteamAPI_Init</c> 是否成功。</param>
public sealed record SteamHostMineResult(
    bool Ok,
    IReadOnlyList<ulong> Ids,
    int Total,
    bool Truncated,
    string? Error,
    bool Initialized)
{
    /// <summary>单次宿主动作最多回传的条目数（20 页 × 50）。</summary>
    public const int MaxItems = 1000;

    /// <summary>失败结果。</summary>
    public static SteamHostMineResult Fail(string error) =>
        new(false, Array.Empty<ulong>(), -1, false, error, false);
}

/// <summary>
/// 宿主按 id 读「自己某个工坊条目」详情的结果。
/// <para>
/// 对应 <c>ISteamRemoteStorage::GetPublishedFileDetails</c>（鉴权版）。<see cref="Visibility"/>
/// 是 **Steam 侧事实**，与发布记录里「上次提交时想设成什么」的
/// <see cref="SteamWorkshopVisibility"/> 不是一回事。
/// </para>
/// </summary>
/// <param name="Ok">是否读到了详情。</param>
/// <param name="PublishedFileId">条目 id（回显）。</param>
/// <param name="Title">标题。</param>
/// <param name="Description">描述（BBCode 原文）。</param>
/// <param name="Tags">标签（宿主已按逗号拆开）。</param>
/// <param name="Visibility">可见性 0 公开 / 1 仅好友 / 2 私密 / 3 未列出。</param>
/// <param name="OwnerSteamId64">作者 SteamID64。</param>
/// <param name="FileSizeBytes">内容字节数。</param>
/// <param name="CreatedUnix">创建时间（Unix 秒）；0 表示没读到。</param>
/// <param name="UpdatedUnix">更新时间（Unix 秒）；0 表示没读到。</param>
/// <param name="CreatorAppId">发布时用的 AppID（可用于识别「发到别的游戏里了」）。</param>
/// <param name="Error">失败原因（中文，直接进日志 / UI）。</param>
public sealed record SteamHostMineItemResult(
    bool Ok,
    ulong PublishedFileId,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    int Visibility,
    ulong OwnerSteamId64,
    long FileSizeBytes,
    uint CreatedUnix,
    uint UpdatedUnix,
    int CreatorAppId,
    string? Error)
{
    /// <summary>失败结果。</summary>
    public static SteamHostMineItemResult Fail(ulong publishedFileId, string error) =>
        new(false, publishedFileId, string.Empty, string.Empty, Array.Empty<string>(), -1, 0, 0, 0, 0, 0, error);
}

/// <summary>批量动作请求；由 App 侧序列化、base64 后通过 <c>--request</c> 传给宿主。</summary>
/// <param name="Op">动作，见 <see cref="WorkshopBatchOps"/>。</param>
/// <param name="Ids">工坊条目 id 列表。</param>
/// <param name="Flag">动作的布尔参数（目前只有「本地禁用」用得到）。</param>
public sealed record WorkshopBatchRequest(string Op, IReadOnlyList<ulong> Ids, bool Flag = true);

/// <summary>批量动作结果里的一条。</summary>
/// <param name="PublishedFileId">工坊条目 id。</param>
/// <param name="Ok">这一条是否成功。</param>
/// <param name="State">动作之后的安装状态（成功时通常有值）。</param>
/// <param name="Error">这一条失败的原因；成功时为 null。</param>
public sealed record WorkshopBatchItem(ulong PublishedFileId, bool Ok, WorkshopInstallState? State, string? Error);

/// <summary>批量动作的最终结果。</summary>
/// <param name="Ok">所有条目都成功时为 true。</param>
/// <param name="Op">回显的动作名。</param>
/// <param name="Items">逐条结果。</param>
/// <param name="Error">整体失败原因（例如原生库缺失、参数不对）。</param>
/// <param name="Initialized"><c>SteamAPI_Init</c> 是否成功。</param>
public sealed record WorkshopBatchResult(
    bool Ok,
    string Op,
    IReadOnlyList<WorkshopBatchItem> Items,
    string? Error,
    bool Initialized)
{
    /// <summary>一次批量动作最多处理的条目数。</summary>
    public const int MaxItems = 512;

    /// <summary>失败结果。</summary>
    public static WorkshopBatchResult Fail(string op, string error) =>
        new(false, op, Array.Empty<WorkshopBatchItem>(), error, false);
}

/// <summary>成就列表里的一条（宿主 → App 的 JSON 载荷）。</summary>
/// <remarks>
/// 用 init 属性而不是位置构造参数：<c>System.Text.Json</c> 对缺失字段会保留默认值，
/// 这样「老宿主少给一个字段」不会让反序列化整体失败。字符串一律给空串默认值，
/// 避免调用方到处判 null。
/// </remarks>
public sealed record SteamHostAchievementItem
{
    /// <summary>成就的 API 名（<c>GetAchievementName</c> 的返回值，也是页面上行的稳定 id）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>显示名（<c>GetAchievementDisplayAttribute(name, "name")</c>）；宿主读不到时为空，由 App 退回 API 名。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>说明文字（<c>GetAchievementDisplayAttribute(name, "desc")</c>）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>是否隐藏成就（未解锁时 Steam 也不给看说明）。</summary>
    public bool Hidden { get; init; }

    /// <summary>当前账号是否已解锁。</summary>
    public bool Achieved { get; init; }

    /// <summary>解锁时间（Unix 秒）；0 表示未解锁或该成就没有时间戳。</summary>
    public uint UnlockTime { get; init; }

    /// <summary>全球解锁率（0-100）；读不到时为 null。</summary>
    public double? GlobalPercent { get; init; }
}

/// <summary>
/// 宿主执行一次成就读取后回报的结果。
/// <para>
/// <see cref="Code"/> 是**稳定结果码**（见 <see cref="SteamAchievementCodes"/>）：
/// 「Steam 没启动」「Init 失败」「等回调超时」「这款游戏一个成就都没有」必须能分开判读，
/// 否则页面只能把「Steam 没运行」显示成「没有成就」。
/// </para>
/// </summary>
public sealed record SteamHostAchievementsResult
{
    /// <summary>是否读到了成就列表（<see cref="Code"/> 为 <see cref="SteamAchievementCodes.Ok"/>）。</summary>
    public bool Ok { get; init; }

    /// <summary>结果码，见 <see cref="SteamAchievementCodes"/>。</summary>
    public string Code { get; init; } = SteamAchievementCodes.Ok;

    /// <summary><c>SteamAPI_Init</c> 是否成功（= 本进程被 Steam 登记过）。</summary>
    public bool Initialized { get; init; }

    /// <summary>成就列表；成功但为 0 条时是空数组，配合 <see cref="SteamAchievementCodes.NoAchievements"/> 才有意义。</summary>
    public IReadOnlyList<SteamHostAchievementItem> Items { get; init; } = Array.Empty<SteamHostAchievementItem>();

    /// <summary>失败原因（中文，直接进日志）。</summary>
    public string? Error { get; init; }

    /// <summary>原生错误文本（Init 的 message buffer 等）。</summary>
    public string? Detail { get; init; }

    /// <summary>成功结果。</summary>
    public static SteamHostAchievementsResult Success(IReadOnlyList<SteamHostAchievementItem> items) =>
        new() { Ok = true, Code = SteamAchievementCodes.Ok, Initialized = true, Items = items };

    /// <summary>明确「这款游戏没有成就」的结果（不是失败，但也不是可用数据）。</summary>
    public static SteamHostAchievementsResult None(bool initialized) =>
        new() { Ok = false, Code = SteamAchievementCodes.NoAchievements, Initialized = initialized };

    /// <summary>失败结果。</summary>
    public static SteamHostAchievementsResult Fail(string code, string? error = null, string? detail = null, bool initialized = false) =>
        new() { Ok = false, Code = code, Initialized = initialized, Error = error, Detail = detail };
}

/// <summary>
/// 宿主对一份 <c>steam_api64.dll</c> 做导出表探测的结果。
/// <para>
/// 只有 <see cref="Ok"/> 为 true 的候选才有资格去跑 <see cref="SteamHostProtocol.AchievementsVerb"/>；
/// 不合格的典型是只挂 DLC 解锁的 loader（缺 <c>SteamAPI_ISteamUserStats_GetNumAchievements</c>）。
/// </para>
/// </summary>
public sealed record SteamHostApiCheckResult
{
    /// <summary>是否具备读成就所需的导出。</summary>
    public bool Ok { get; init; }

    /// <summary>不合格原因（中文，直接进日志）。</summary>
    public string? Error { get; init; }

    /// <summary>合格。</summary>
    public static SteamHostApiCheckResult Success() => new() { Ok = true };

    /// <summary>不合格。</summary>
    public static SteamHostApiCheckResult Fail(string? error) => new() { Ok = false, Error = error };
}
