namespace PdxLauncher.Checks;

using System.Globalization;
using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Storage;

/// <summary>
/// 「已经发布到创意工坊，启动器却还说未发布 / 不存在」这条链路的自检。
/// <para>
/// 三件事各自都会单独把用户带偏：id 解析放宽（把 0 或全角数字当合法编号）、
/// 发布记录里的 <c>SteamVisibility</c> 判定反了（把私密条目显示成「已发布」且没有任何提示）、
/// 以及宿主结果行的 JSON 契约漂移（枚举 / 详情读回来是空列表，界面就说「一条都没有」）。
/// 这三样都不会崩，只在真机上表现为「明明传了却显示未发布」，编译器看不见，所以钉在这里。
/// </para>
/// 不联网、不启动任何进程。
/// </summary>
public static class ModWorkshopBindingChecks
{
    /// <summary>与 <see cref="ModPublicationStore"/> 一致的序列化口径。</summary>
    private static readonly JsonSerializerOptions StoreJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckIdInput();
        CheckPublicationStates();
        CheckPublicationJson();
        CheckRowTextKeys();
        CheckHostProtocol();
        CheckDetailResult();
        CheckNativeLayout();
        CheckStoreKeyStability();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>绑定框的 id 解析：空 = 还没填（合法），非空必须是大于 0 的 ASCII 十进制整数。</summary>
    private static void CheckIdInput()
    {
        foreach (var empty in new string?[] { null, string.Empty, "   " })
        {
            ChecksContext.Check(
                "id 解析：空输入合法且不产生编号（" + (empty ?? "null") + "）",
                ModIdInput.TryParse(empty, out var none) && none is null);
        }

        ChecksContext.Check(
            "id 解析：正常编号",
            ModIdInput.TryParse("3832809800", out var parsed) && parsed == 3832809800UL);

        ChecksContext.Check(
            "id 解析：前后空白会被裁掉",
            ModIdInput.TryParse("  3832809800  ", out var trimmed) && trimmed == 3832809800UL);

        // 「0」会被当成「还没填」还是「填了但是 0」必须说清楚：后者会让 SetItemUpdate 绑到不存在的条目。
        foreach (var bad in new[] { "0", "-1", "+1", "1.5", "1_000", "3832809800abc", "１" })
        {
            ChecksContext.Check("id 解析：拒绝写法 " + bad, !ModIdInput.TryParse(bad, out _));
        }
    }

    /// <summary>
    /// 发布状态四态里的「已发布（未公开）」判据：<c>SteamVisibility</c> 是 Steam 侧事实，
    /// 0 与 null（没核实）按可公开处理，1/2/3 一律算「限制了可见性」。
    /// </summary>
    private static void CheckPublicationStates()
    {
        var noInfo = Publication(steamVisibility: null);
        ChecksContext.Check("状态：没核实过可见性时按可公开处理", noInfo.IsPubliclyVisible);
        ChecksContext.Check("状态：没核实过时不算未公开", !noInfo.IsUnlisted);

        var pub = Publication(steamVisibility: 0);
        ChecksContext.Check("状态：Steam 公开", pub.IsPubliclyVisible && !pub.IsUnlisted);

        foreach (var hidden in new[] { 1, 2, 3 })
        {
            var item = Publication(steamVisibility: hidden);
            ChecksContext.Check(
                "状态：Steam 可见性 " + hidden + " 算未公开",
                item.IsUnlisted && !item.IsPubliclyVisible);
        }

        // 「提交时想设成什么」与「现在实际是什么」必须各说各的：
        // 私密条目上 Intent=Public 是常见现场（用户上传时选了公开，之后在网页上改成私密）。
        var conflict = new ModPublication(
            "victoria3",
            @"C:\Mods\ModA",
            3832809800UL,
            "t",
            "d",
            Array.Empty<string>(),
            true,
            null,
            null,
            DateTimeOffset.UnixEpoch,
            SteamWorkshopVisibility.Public,
            SteamVisibility: 2);
        ChecksContext.Equal("状态：意图可见性保留公开", SteamWorkshopVisibility.Public, conflict.EffectiveVisibility);
        ChecksContext.Check("状态：事实可见性仍是私密", conflict.IsUnlisted);
    }

    /// <summary>发布记录 JSON 往返：新字段要能存下来，旧记录（没有 steamVisibility）读成 null。</summary>
    private static void CheckPublicationJson()
    {
        var original = new ModPublication(
            "victoria3",
            @"C:\Mods\ModA",
            3832809800UL,
            "虚空造物+货运红利",
            "描述",
            new[] { "Gameplay" },
            false,
            @"C:\Mods\ModA\thumbnail.png",
            null,
            new DateTimeOffset(2026, 11, 3, 23, 0, 0, TimeSpan.FromHours(8)),
            SteamWorkshopVisibility.Private,
            ContentDescriptors: new[] { 1, 3 },
            SteamVisibility: 3);

        var json = JsonSerializer.Serialize(original, StoreJson);
        ChecksContext.Check("JSON：写出 steamVisibility", json.Contains("\"steamVisibility\": 3", StringComparison.Ordinal));

        var back = JsonSerializer.Deserialize<ModPublication>(json, StoreJson);
        ChecksContext.Check("JSON：能读回记录", back is not null);
        if (back is not null)
        {
            ChecksContext.Equal("JSON：编号", original.PublishedFileId, back.PublishedFileId);
            ChecksContext.Equal("JSON：标题", original.Title, back.Title);
            ChecksContext.Equal("JSON：标签", string.Join(",", original.Tags), string.Join(",", back.Tags));
            ChecksContext.Equal("JSON：可见性", SteamWorkshopVisibility.Private, back.EffectiveVisibility);
            ChecksContext.Equal("JSON：Steam 可见性", 3, back.SteamVisibility);
            ChecksContext.Equal("JSON：更新时间", original.UpdatedAt, back.UpdatedAt);
        }

        // 旧记录：字段不存在时必须读成 null（不能默认成 0 冒充「Steam 说它公开」）。
        const string Legacy =
            """{"gameId":"victoria3","modPath":"C:\\Mods\\ModA","publishedFileId":3013335017,"title":"t","description":"d","tags":[],"public":false,"previewPath":null,"lastError":null,"updatedAt":"2026-11-03T23:00:00+08:00"}""";
        var legacy = JsonSerializer.Deserialize<ModPublication>(Legacy, StoreJson);
        ChecksContext.Check("JSON：旧记录能读", legacy is not null);
        ChecksContext.Equal("JSON：旧记录没有 Steam 可见性", null, legacy?.SteamVisibility);
        ChecksContext.Check("JSON：旧记录不算未公开", legacy is not null && !legacy.IsUnlisted);
    }

    /// <summary>四态行文案的键必须在两份 modstudio.json 里真实存在（Core 引用不到 App 生成的 Keys）。</summary>
    private static void CheckRowTextKeys()
    {
        string[] keys =
        [
            "modstudio.rowPublished",
            "modstudio.rowPublishedUnlisted",
            "modstudio.rowPublishFailed",
            "modstudio.rowUnpublished",
            "modstudio.bindExisting",
            "modstudio.bindHint",
            "modstudio.bindTitle",
            "modstudio.bindBody",
            "modstudio.bindIdLabel",
            "modstudio.bindLoadMine",
            "modstudio.bindMyItemsEmpty",
            "modstudio.bindSuccess",
            "modstudio.bindUnverified",
            "modstudio.bindFailed",
            "modstudio.detailSteamVisibility",
            "modstudio.visibilityPublic",
            "modstudio.visibilityFriendsOnly",
            "modstudio.visibilityPrivate",
            "modstudio.visibilityUnlisted",
            "modstudio.visibilityUnknown",
        ];

        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var path = RepoPath(Path.Combine("tools", "i18n", language, "modstudio.json"));
            if (!File.Exists(path))
            {
                ChecksContext.Check("文案表存在：" + language, false, path);
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (var key in keys)
            {
                ChecksContext.Check(
                    language + " 有键 " + key,
                    text.Contains("\"" + key + "\":", StringComparison.Ordinal),
                    path);
            }
        }
    }

    /// <summary>新动词与两条结果记录的契约：编出去的行必须能被客户端原样解回来。</summary>
    private static void CheckHostProtocol()
    {
        ChecksContext.Check(
            "协议：workshop-mine 在已知动词里",
            SteamHostProtocol.Verbs.Contains(SteamHostProtocol.WorkshopMineVerb));
        ChecksContext.Check(
            "协议：workshop-mine-detail 在已知动词里",
            SteamHostProtocol.Verbs.Contains(SteamHostProtocol.WorkshopMineDetailVerb));

        string[] args = [SteamHostProtocol.VerbArgument, SteamHostProtocol.WorkshopMineVerb];
        ChecksContext.Check(
            "协议：能认出 workshop-mine 动词",
            SteamHostProtocol.TryGetVerb(args, out var verb) && verb == SteamHostProtocol.WorkshopMineVerb);

        var mine = new SteamHostMineResult(
            true,
            new[] { 3832809800UL, 3613769385UL },
            2,
            false,
            null,
            true);
        var mineLine = SteamHostProtocol.Encode(mine);
        ChecksContext.Check(
            "协议：我的条目结果行能解回",
            SteamHostProtocol.TryDecode<SteamHostMineResult>(mineLine + Environment.NewLine, out var mineBack)
            && mineBack is not null
            && mineBack.Ids.Count == 2
            && mineBack.Ids[0] == 3832809800UL
            && mineBack.Total == 2
            && mineBack.Initialized);

        var item = new SteamHostMineItemResult(
            true,
            3832809800UL,
            "虚空造物+货运红利",
            "描述",
            new[] { "Gameplay", "Utilities" },
            2,
            76561198370719182UL,
            897287L,
            1793717000U,
            1793718000U,
            529340,
            null);
        var itemLine = SteamHostProtocol.Encode(item);
        ChecksContext.Check(
            "协议：我的条目详情能解回",
            SteamHostProtocol.TryDecode<SteamHostMineItemResult>(itemLine, out var itemBack)
            && itemBack is not null
            && itemBack.PublishedFileId == 3832809800UL
            && itemBack.Visibility == 2
            && itemBack.Tags.Count == 2
            && itemBack.CreatorAppId == 529340
            && itemBack.OwnerSteamId64 == 76561198370719182UL);

        ChecksContext.Check(
            "协议：失败结果带错误文本",
            SteamHostMineResult.Fail("Steam 没开").Error == "Steam 没开");
        ChecksContext.Check(
            "协议：详情失败时可见性为 -1（未读到）",
            SteamHostMineItemResult.Fail(1, "x").Visibility == -1);
    }

    /// <summary>
    /// 匿名调用方对隐藏条目拿到的是 <c>result:9</c>；旧实现把它当一条空条目返回，
    /// 界面就成了一张空卡片。要确保 (null, 9) 这个结论不被「顺手修回去」。
    /// </summary>
    private static void CheckDetailResult()
    {
        const string Hidden =
            """{"response":{"result":1,"resultcount":1,"publishedfiledetails":[{"publishedfileid":"3832809800","result":9}]}}""";
        var (detail, result) = SteamWorkshopClient.ParseDetailWithResult(Hidden);
        ChecksContext.Check("详情：隐藏条目没有详情", detail is null);
        ChecksContext.Equal("详情：隐藏条目的 EResult", 9, result);
        ChecksContext.Check(
            "详情：9 的原因文本说明「不等于不存在」",
            SteamWorkshopClient.DetailUnavailableReason(9).Contains("不等于它不存在", StringComparison.Ordinal));

        const string Visible =
            """{"response":{"result":1,"resultcount":1,"publishedfiledetails":[{"publishedfileid":"3832809800","result":1,"title":"Void Creation","file_size":"897287"}]}}""";
        var (visible, okResult) = SteamWorkshopClient.ParseDetailWithResult(Visible);
        ChecksContext.Equal("详情：可见条目的 EResult", 1, okResult);
        ChecksContext.Equal("详情：标题", "Void Creation", visible?.Title);
        ChecksContext.Equal("详情：大小", 897287L, visible?.FileSizeBytes);
        ChecksContext.Check("详情：ParseDetail 仍然等价", SteamWorkshopClient.ParseDetail(Visible)?.Title == "Void Creation");

        var (bad, badResult) = SteamWorkshopClient.ParseDetailWithResult("not json");
        ChecksContext.Check("详情：坏响应没有详情", bad is null);
        ChecksContext.Equal("详情：坏响应的哨兵值", SteamWorkshopClient.ResultUnknown, badResult);

        var (missing, missingResult) = SteamWorkshopClient.ParseDetailWithResult(
            """{"response":{"result":1,"resultcount":0,"publishedfiledetails":[]}}""");
        ChecksContext.Check("详情：空列表没有详情", missing is null);
        ChecksContext.Equal("详情：空列表也是哨兵值", SteamWorkshopClient.ResultUnknown, missingResult);
    }

    /// <summary>
    /// 原生结构体布局：C# 侧的 <c>Marshal.SizeOf</c> 必须与 Steamworks 头里的字段顺序一致。
    /// 差一个字节就会读到脏的可见性 / 标题，而原生调用本身不会报错。
    /// </summary>
    private static void CheckNativeLayout()
    {
        // 412 字节的枚举结构按 8 字节对齐补到 416；
        // 详情结构是 9753 → 9760（描述缓冲 8000 字节是最大的一块，别再按「看起来像 1760」的直觉改）。
        ChecksContext.Equal("布局：枚举结果结构大小", 416, SteamWorkshopHostOperation.OwnEnumerateResultSize);
        ChecksContext.Equal("布局：条目详情结构大小", 9760, SteamWorkshopHostOperation.OwnDetailResultSize);
    }

    /// <summary>记录键按「游戏 + 规范化路径哈希」生成：大小写与尾部分隔符不能各算一条。</summary>
    private static void CheckStoreKeyStability()
    {
        var documents = new MemoryDocumentStore();
        var store = new ModPublicationStore(documents);

        var publication = Publication(steamVisibility: 0) with
        {
            ModPath = @"C:\Mods\ModA\",
            PublishedFileId = 3832809800UL,
        };

        ChecksContext.Check("存储：写入成功", store.Save(publication));

        var samePath = store.TryGet("victoria3", @"C:\Mods\moda");
        ChecksContext.Check("存储：大小写与尾部分隔符算同一条", samePath is not null);
        ChecksContext.Equal("存储：读回的编号", 3832809800UL, samePath?.PublishedFileId);

        ChecksContext.Check("存储：不同路径互不串", store.TryGet("victoria3", @"C:\Mods\ModB") is null);
        ChecksContext.Check("存储：不同游戏互不串", store.TryGet("ck3", @"C:\Mods\ModA") is null);
        ChecksContext.Equal("存储：按前缀列出 1 条", 1, store.List("victoria3").Count);

        ChecksContext.Check("存储：删除后读不到", store.Delete("victoria3", @"C:\Mods\moda\") && store.TryGet("victoria3", @"C:\Mods\ModA") is null);
    }

    private static ModPublication Publication(int? steamVisibility) => new(
        "victoria3",
        @"C:\Mods\ModA",
        null,
        "t",
        "d",
        Array.Empty<string>(),
        true,
        null,
        null,
        DateTimeOffset.UnixEpoch,
        SteamVisibility: steamVisibility);

    /// <summary>从输出目录往上找仓库根（同其余检查类的做法）。</summary>
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

    /// <summary>内存版文档存储：只为了在不碰真实数据库的前提下验证键的规范化。</summary>
    private sealed class MemoryDocumentStore : IJsonDocumentStore
    {
        private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

        public bool TryRead(string key, out string? json) => _documents.TryGetValue(key, out json);

        public bool Write(string key, string json)
        {
            _documents[key] = json;
            return true;
        }

        public bool Delete(string key) => _documents.Remove(key);

        public bool Exists(string key) => _documents.ContainsKey(key);

        public long MeasureBytes(string prefix) => 0;

        public IReadOnlyList<string> List(string prefix) => _documents.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }
}
