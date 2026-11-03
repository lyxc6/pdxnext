namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 新增的两个宿主动词（订阅列表 <c>workshop-subscribed</c>、批量动作 <c>workshop-batch</c>）的协议自检：
/// 动词白名单、请求/结果的 JSON 往返、批量动作名的写/读分类。
/// </summary>
public static class WorkshopSubscribedProtocolChecks
{
    /// <summary>运行本组检查。</summary>
    /// <param name="fileSystem">文件系统（本组不用，参数保留以统一入口）。</param>
    /// <returns>0 表示全部通过。</returns>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        try
        {
            CheckVerbsInWhitelist();
            CheckSubscribedRoundTrip();
            CheckBatchRequestRoundTrip();
            CheckBatchResultRoundTrip();
            CheckBatchOpsClassification();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("新增宿主动词：未抛出异常", false, ex.Message);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckVerbsInWhitelist()
    {
        ChecksContext.Check(
            "动词白名单：含 workshop-subscribed",
            SteamHostProtocol.Verbs.Contains(SteamHostProtocol.WorkshopSubscribedVerb));

        ChecksContext.Check(
            "动词白名单：含 workshop-batch",
            SteamHostProtocol.Verbs.Contains(SteamHostProtocol.WorkshopBatchVerb));

        // 旧动词不能被新动词挤掉：详情页订阅与 Mod 创建上传都还指着它们。
        ChecksContext.Check(
            "动词白名单：原有 workshop-publish 仍在",
            SteamHostProtocol.Verbs.Contains(SteamHostProtocol.WorkshopPublishVerb));

        ChecksContext.Check(
            "动词白名单：未知动词被拒",
            !SteamHostProtocol.TryGetVerb([SteamHostProtocol.VerbArgument, "workshop-everything"], out _));

        ChecksContext.Check(
            "动词白名单：新动词被识别",
            SteamHostProtocol.TryGetVerb(
                [SteamHostProtocol.VerbArgument, SteamHostProtocol.WorkshopSubscribedVerb],
                out var verb) && verb == SteamHostProtocol.WorkshopSubscribedVerb);
    }

    private static void CheckSubscribedRoundTrip()
    {
        var original = new SteamHostSubscribedResult(
            Ok: true,
            Items:
            [
                new WorkshopSubscribedItem(
                    1234567,
                    new WorkshopInstallState(
                        IsSubscribed: true,
                        IsInstalled: true,
                        IsDownloading: false,
                        AcfPath: null,
                        NeedsUpdate: true,
                        IsDisabledLocally: false,
                        IsDownloadPending: false,
                        SizeOnDisk: 4096,
                        Folder: "1234567",
                        BytesDownloaded: null,
                        BytesTotal: null)),
                new WorkshopSubscribedItem(7654321, WorkshopInstallState.Unknown),
            ],
            Error: null,
            Initialized: true,
            Truncated: true);

        var line = SteamHostProtocol.Encode(original);
        ChecksContext.Check("订阅列表：输出带结果前缀", line.StartsWith(SteamHostProtocol.ResultPrefix, StringComparison.Ordinal));

        ChecksContext.Check("订阅列表：能解回来", SteamHostProtocol.TryDecode<SteamHostSubscribedResult>(line, out var decoded));
        ChecksContext.Equal("订阅列表：Ok 往返", true, decoded!.Ok);
        ChecksContext.Equal("订阅列表：条数往返", 2, decoded.Items.Count);
        ChecksContext.Equal("订阅列表：id 往返", 1234567UL, decoded.Items[0].PublishedFileId);
        ChecksContext.Equal("订阅列表：需要更新位往返", true, decoded.Items[0].State.NeedsUpdate);
        ChecksContext.Equal("订阅列表：磁盘占用往返", (ulong?)4096, decoded.Items[0].State.SizeOnDisk);
        ChecksContext.Equal("订阅列表：目录往返", "1234567", decoded.Items[0].State.Folder);
        ChecksContext.Equal("订阅列表：Unknown 条目仍保留 id", 7654321UL, decoded.Items[1].PublishedFileId);
        ChecksContext.Equal("订阅列表：截断标记往返", true, decoded.Truncated);
        ChecksContext.Equal("订阅列表：Initialized 往返", true, decoded.Initialized);

        var failed = SteamHostSubscribedResult.Fail("steam_api64.dll 缺少 GetSubscribedItems");
        ChecksContext.Equal("订阅列表：失败结果 Ok=false", false, failed.Ok);
        ChecksContext.Equal("订阅列表：失败结果没有条目", 0, failed.Items.Count);
        ChecksContext.Equal("订阅列表：失败结果带原因", "steam_api64.dll 缺少 GetSubscribedItems", failed.Error);
    }

    private static void CheckBatchRequestRoundTrip()
    {
        var request = new WorkshopBatchRequest(
            WorkshopBatchOps.Disable,
            [111UL, 222UL],
            Flag: true);

        var encoded = SteamHostProtocol.Encode(request);
        ChecksContext.Check(
            "批量请求：能解回来",
            SteamHostProtocol.TryDecode<WorkshopBatchRequest>(encoded, out var decoded));

        ChecksContext.Equal("批量请求：Op 往返", WorkshopBatchOps.Disable, decoded!.Op);
        ChecksContext.Equal("批量请求：id 条数往返", 2, decoded.Ids.Count);
        ChecksContext.Equal("批量请求：id 顺序往返", 222UL, decoded.Ids[1]);
        ChecksContext.Equal("批量请求：Flag 往返", true, decoded.Flag);
    }

    private static void CheckBatchResultRoundTrip()
    {
        var original = new WorkshopBatchResult(
            Ok: false,
            Op: WorkshopBatchOps.Download,
            Items:
            [
                new WorkshopBatchItem(111, true, new WorkshopInstallState(true, false, true, null), null),
                new WorkshopBatchItem(222, false, null, "条目不存在"),
            ],
            Error: null,
            Initialized: true);

        var line = SteamHostProtocol.Encode(original);
        ChecksContext.Check(
            "批量结果：能解回来",
            SteamHostProtocol.TryDecode<WorkshopBatchResult>(line, out var decoded));

        ChecksContext.Equal("批量结果：Ok 往返", false, decoded!.Ok);
        ChecksContext.Equal("批量结果：Op 往返", WorkshopBatchOps.Download, decoded.Op);
        ChecksContext.Equal("批量结果：条数往返", 2, decoded.Items.Count);
        ChecksContext.Equal("批量结果：第一条成功", true, decoded.Items[0].Ok);
        ChecksContext.Equal("批量结果：第一条状态带回下载中", true, decoded.Items[0].State!.IsDownloading);
        ChecksContext.Equal("批量结果：第二条失败原因往返", "条目不存在", decoded.Items[1].Error);
        ChecksContext.Equal("批量结果：失败的条目没有状态", null, decoded.Items[1].State);

        var failed = WorkshopBatchResult.Fail(WorkshopBatchOps.State, "ISteamUGC 不可用");
        ChecksContext.Equal("批量结果：整体失败 Ok=false", false, failed.Ok);
        ChecksContext.Equal("批量结果：整体失败回显 Op", WorkshopBatchOps.State, failed.Op);
        ChecksContext.Equal("批量结果：整体失败没有逐条结果", 0, failed.Items.Count);
    }

    private static void CheckBatchOpsClassification()
    {
        ChecksContext.Equal("批量动作：共四个", 4, WorkshopBatchOps.All.Count);

        ChecksContext.Check("批量动作：state 是只读", !WorkshopBatchOps.IsWrite(WorkshopBatchOps.State));
        ChecksContext.Check("批量动作：download 是写", WorkshopBatchOps.IsWrite(WorkshopBatchOps.Download));
        ChecksContext.Check("批量动作：disable 是写", WorkshopBatchOps.IsWrite(WorkshopBatchOps.Disable));
        ChecksContext.Check("批量动作：enable 是写", WorkshopBatchOps.IsWrite(WorkshopBatchOps.Enable));
        ChecksContext.Check("批量动作：未知动作不算写", !WorkshopBatchOps.IsWrite("delete-item"));
        ChecksContext.Check("批量动作：null 不算写", !WorkshopBatchOps.IsWrite(null));
    }
}
