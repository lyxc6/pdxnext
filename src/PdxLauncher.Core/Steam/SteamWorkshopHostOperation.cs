namespace PdxLauncher.Core.Steam;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// 在**短命子进程里**执行一次工坊操作（读状态 / 订阅 / 取消订阅 / 发布）。
/// <para>
/// 这里刻意不用 <c>DllImport</c>：宿主每次运行只加载命令行指定的那一份
/// <c>steam_api64.dll</c>（游戏目录里的那份，可能被代理 DLL 补丁替换过），
/// 进程退出即释放，既不会串进程，也不会让 Steam 的登记残留到启动器身上。
/// </para>
/// <para>本类型只允许在 <c>--steam-host</c> 子进程里使用；主启动器进程里一次都不许调 <c>Init</c>。</para>
/// </summary>
internal sealed class SteamWorkshopHostOperation : IDisposable
{
    private const string AppIdVariable = "SteamAppId";
    private const int InitFlatBufferLength = 1024;
    private const int ResultOk = 1;
    private const int CreateItemResultId = 3403;
    private const int SubmitItemUpdateResultId = 3404;
    private const int SteamApiCallCompletedId = 703;

    /// <summary><c>k_iSteamRemoteStorageCallbacks = 1300</c>（见 sdk/public/steam/steam_api_internal.h:258）+ 12 / + 18。</summary>
    private const int OwnEnumerateResultId = 1312;
    private const int OwnDetailResultId = 1318;

    /// <summary>EResult 9 = FileNotFound；匿名 Web API 对未公开条目也回这个值。</summary>
    private const int FileNotFoundResult = 9;

    /// <summary>每次枚举最多回 50 条（<c>k_unEnumeratePublishedFilesMaxResults</c>，isteamremotestorage.h:55）。</summary>
    private const int OwnPageSize = 50;

    /// <summary>枚举自己发布的条目时最多翻多少页（50 × 20 = 1000 条）。</summary>
    private const int MaxOwnPages = 20;

    /// <summary>内容描述词 id 的下界（EUGCContentDescriptorID 从 1 开始）。</summary>
    private const int ContentDescriptorFirst = 1;

    /// <summary>内容描述词 id 的上界（AnyMatureContent = 5）。</summary>
    private const int ContentDescriptorLast = 5;

    /// <summary>EItemPreviewType.Image。</summary>
    private const int PreviewTypeImage = 0;

    /// <summary>清空附加预览时的最大删除次数，防止异常情况下死循环。</summary>
    private const int MaxPreviewRemoval = 64;

    /// <summary>多语言阶段的进度名（App 侧映射到 <c>modstudio.stageLocalizing</c>）。</summary>
    private const string LocalizingStage = "localizing";

    private readonly string _apiFilePath;
    private readonly int _appId;

    private IntPtr _library;
    private bool _initialized;
    private string? _previousAppId;
    private IntPtr _ugc;
    private int _pipe;
    private bool _manualInitialized;

    private ShutdownFn? _shutdown;
    private RunCallbacksFn? _runCallbacks;
    private SteamUgcFn? _steamUgc;
    private SubscribeItemFn? _subscribeItem;
    private UnsubscribeItemFn? _unsubscribeItem;
    private GetItemStateFn? _getItemState;
    private CreateItemFn? _createItem;
    private StartItemUpdateFn? _startItemUpdate;
    private SubmitItemUpdateFn? _submitItemUpdate;
    private SetItemTextFn? _setItemTitle;
    private SetItemTextFn? _setItemDescription;
    private SetItemTextFn? _setItemContent;
    private SetItemTextFn? _setItemPreview;
    private SetItemVisibilityFn? _setItemVisibility;
    private SetItemTagsFn? _setItemTags;
    private SetItemTextFn? _setItemUpdateLanguage;
    private AddItemPreviewFileFn? _addItemPreviewFile;
    private AddItemPreviewVideoFn? _addItemPreviewVideo;
    private RemoveItemPreviewFn? _removeItemPreview;
    private ContentDescriptorFn? _addContentDescriptor;
    private ContentDescriptorFn? _removeContentDescriptor;
    private GetItemUpdateProgressFn? _getItemUpdateProgress;
    private GetNumSubscribedItemsFn? _getNumSubscribedItems;
    private GetSubscribedItemsFn? _getSubscribedItems;
    private GetItemInstallInfoFn? _getItemInstallInfo;
    private GetItemDownloadInfoFn? _getItemDownloadInfo;
    private DownloadItemFn? _downloadItem;
    private SetItemsDisabledLocallyFn? _setItemsDisabledLocally;
    private GetHSteamPipeFn? _getHSteamPipe;
    private ManualDispatchInitFn? _manualDispatchInit;
    private ManualDispatchRunFrameFn? _manualDispatchRunFrame;
    private ManualDispatchGetNextCallbackFn? _manualDispatchGetNextCallback;
    private ManualDispatchFreeLastCallbackFn? _manualDispatchFreeLastCallback;
    private ManualDispatchGetApiCallResultFn? _manualDispatchGetApiCallResult;

    // ISteamRemoteStorage 这一组只在「查我自己发布的条目」时用到：它不依赖 UGC 版本，
    // 且旧版 / 裁剪过的 dll 可能没有，所以单独探测、单独判可用，不参与 EnsureInitialized 的硬门槛。
    private SteamRemoteStorageFn? _steamRemoteStorage;
    private IntPtr _remoteStorage;
    private SteamRemoteStorageEnumFn? _rsEnumUserPublishedFiles;
    private SteamRemoteStorageDetailFn? _rsGetPublishedFileDetails;
    private OwnEnumerateFilesResult? _ownEnumerateResult;
    private OwnFileDetailResult? _ownDetailResult;

    private CreateItemResult? _createResult;
    private SubmitItemUpdateResult? _submitResult;
    private string? _callbackError;

    /// <summary>构造。</summary>
    /// <param name="apiFilePath">要加载的 <c>steam_api64.dll</c> 全路径。</param>
    /// <param name="appId">游戏的 Steam appid。</param>
    public SteamWorkshopHostOperation(string apiFilePath, int appId)
    {
        _apiFilePath = apiFilePath;
        _appId = appId;
    }

    /// <summary>当前进程是否已经以该 appid 调过 <c>SteamAPI_Init</c>。</summary>
    public bool Initialized => _initialized;

    /// <summary>读一条工坊条目的安装状态。</summary>
    public SteamHostWorkshopResult ReadState(ulong publishedFileId)
    {
        var error = EnsureInitialized();
        if (error is not null)
        {
            return SteamHostWorkshopResult.Fail(error);
        }

        var state = ReadStateDetail(publishedFileId);
        return new SteamHostWorkshopResult(true, state, null, _initialized);
    }

    /// <summary>订阅 / 取消订阅，并等到 Steam 确认状态翻转（最多 <paramref name="timeout"/>）。</summary>
    public SteamHostWorkshopResult SetSubscribed(ulong publishedFileId, bool subscribe, TimeSpan timeout)
    {
        var error = EnsureInitialized();
        if (error is not null)
        {
            return SteamHostWorkshopResult.Fail(error);
        }

        if (_ugc == IntPtr.Zero)
        {
            return new SteamHostWorkshopResult(false, null, "ISteamUGC 不可用", _initialized);
        }

        try
        {
            if (subscribe)
            {
                _subscribeItem!(_ugc, publishedFileId);
            }
            else
            {
                _unsubscribeItem!(_ugc, publishedFileId);
            }
        }
        catch (Exception ex)
        {
            return new SteamHostWorkshopResult(false, null, "订阅调用异常：" + ex.Message, _initialized);
        }

        var deadline = DateTime.UtcNow + timeout;
        var state = ReadStateForConfirm(publishedFileId);
        while (DateTime.UtcNow < deadline)
        {
            PumpCallbacks();
            state = ReadStateForConfirm(publishedFileId);
            if (subscribe ? state.IsSubscribed == true : state.IsSubscribed == false)
            {
                break;
            }

            Thread.Sleep(250);
        }

        return new SteamHostWorkshopResult(true, state, null, _initialized);
    }

    /// <summary>
    /// 一次拉取当前账号在本游戏下订阅的全部工坊条目。
    /// <para>
    /// 这是「每行起一个宿主子进程」的替代品：一次 Init 换回整张订阅表。库里几百个 mod 时
    /// 差别从「几百次进程启动」变成「一次」。条数上限 <see cref="SteamHostSubscribedResult.MaxItems"/>，
    /// 超出的部分置 <c>Truncated</c>，不静默截断。
    /// </para>
    /// </summary>
    /// <param name="timeout">等待 Steam 给出订阅列表的最长时间。</param>
    /// <returns>订阅列表结果；单条状态读取失败不影响整批。</returns>
    public SteamHostSubscribedResult ReadSubscribed(TimeSpan timeout)
    {
        var error = EnsureInitialized();
        if (error is not null)
        {
            return SteamHostSubscribedResult.Fail(error);
        }

        if (!EnsureSubscriptionBindings())
        {
            return SteamHostSubscribedResult.Fail("steam_api64.dll 缺少读取订阅列表所需的 Steamworks 导出（GetSubscribedItems 等）");
        }

        // Steam 需要先把订阅集合从服务端同步下来；刚启动的宿主进程里第一次调用
        // 常常返回 0，得泵一会儿回调再试。超时仍为 0 就按「空订阅」处理，不当成失败。
        var deadline = DateTime.UtcNow + timeout;
        uint count = 0;
        while (true)
        {
            count = SafeGetNumSubscribedItems();
            if (count > 0 || DateTime.UtcNow >= deadline)
            {
                break;
            }

            PumpCallbacks();
            Thread.Sleep(150);
        }

        if (count == 0)
        {
            return new SteamHostSubscribedResult(true, Array.Empty<WorkshopSubscribedItem>(), null, _initialized);
        }

        var truncated = count > SteamHostSubscribedResult.MaxItems;
        var capacity = truncated ? SteamHostSubscribedResult.MaxItems : (int)count;
        var ids = new ulong[capacity];
        uint written;
        try
        {
            written = _getSubscribedItems!(_ugc, ids, (uint)capacity, false);
        }
        catch (Exception ex)
        {
            return SteamHostSubscribedResult.Fail("读取订阅列表异常：" + ex.Message);
        }

        var items = new List<WorkshopSubscribedItem>((int)Math.Min(written, (uint)capacity));
        for (var i = 0; i < written && i < capacity; i++)
        {
            var id = ids[i];
            if (id == 0)
            {
                continue;
            }

            items.Add(new WorkshopSubscribedItem(id, ReadStateDetail(id)));
        }

        return new SteamHostSubscribedResult(true, items, null, _initialized, truncated);
    }

    /// <summary>
    /// 对一批工坊条目执行同一个动作：只读状态 / 触发下载 / 本地禁用 / 解除本地禁用。
    /// <para>只读动作不会改变任何 Steam 状态，写动作由调用方先用设置开关拦住。</para>
    /// </summary>
    /// <param name="request">动作与条目 id 列表。</param>
    /// <param name="timeout">单条动作后等待状态翻转的上限（整批共用同一个截止时刻）。</param>
    /// <returns>逐条结果；单条失败不影响其它条目。</returns>
    public WorkshopBatchResult RunBatch(WorkshopBatchRequest request, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);

        var op = request.Op ?? string.Empty;
        if (!WorkshopBatchOps.All.Contains(op))
        {
            return WorkshopBatchResult.Fail(op, "未知的批量动作：" + op);
        }

        var ids = (request.Ids ?? Array.Empty<ulong>()).Where(static id => id != 0).Take(WorkshopBatchResult.MaxItems).ToArray();
        if (ids.Length == 0)
        {
            return new WorkshopBatchResult(true, op, Array.Empty<WorkshopBatchItem>(), null, false);
        }

        var error = EnsureInitialized();
        if (error is not null)
        {
            return WorkshopBatchResult.Fail(op, error);
        }

        var needsWrite = !string.Equals(op, WorkshopBatchOps.State, StringComparison.Ordinal);
        if (needsWrite)
        {
            if (op == WorkshopBatchOps.Download && _downloadItem is null)
            {
                return WorkshopBatchResult.Fail(op, "steam_api64.dll 缺少 DownloadItem 导出");
            }

            if ((op == WorkshopBatchOps.Disable || op == WorkshopBatchOps.Enable) && _setItemsDisabledLocally is null)
            {
                return WorkshopBatchResult.Fail(op, "steam_api64.dll 缺少 SetItemsDisabledLocally 导出");
            }
        }

        var items = new List<WorkshopBatchItem>(ids.Length);
        var deadline = DateTime.UtcNow + timeout;
        foreach (var id in ids)
        {
            try
            {
                var applied = op switch
                {
                    WorkshopBatchOps.Download => _downloadItem!(_ugc, id, true),
                    WorkshopBatchOps.Disable => _setItemsDisabledLocally!(_ugc, new[] { id }, 1, true),
                    WorkshopBatchOps.Enable => _setItemsDisabledLocally!(_ugc, new[] { id }, 1, false),
                    _ => true,
                };

                if (!applied)
                {
                    items.Add(new WorkshopBatchItem(id, false, ReadStateDetail(id), "Steam 拒绝了这次调用（" + op + "）"));
                    continue;
                }

                // 写动作后等到位标记真的翻转；读状态不需要等。
                var state = needsWrite ? WaitForState(id, op, deadline) : ReadStateDetail(id);
                items.Add(new WorkshopBatchItem(id, true, state, null));
            }
            catch (Exception ex)
            {
                items.Add(new WorkshopBatchItem(id, false, null, ex.GetType().Name + "：" + ex.Message));
            }
        }

        return new WorkshopBatchResult(items.All(static item => item.Ok), op, items, null, _initialized);
    }

    /// <summary>创建（或更新）一个工坊条目并上传内容；进度通过 <paramref name="progress"/> 回调。</summary>
    public ModPublishResult Publish(
        ModPublishRequest request,
        Action<ModPublishProgress>? progress,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);

        var error = EnsureInitialized();
        if (error is not null)
        {
            return ModPublishResult.Fail(error);
        }

        if (!EnsurePublishBindings())
        {
            return ModPublishResult.Fail("steam_api64.dll 缺少发布所需的 Steamworks 导出");
        }

        if (!_manualInitialized)
        {
            try
            {
                _manualDispatchInit!();
                _pipe = _getHSteamPipe!();
                _manualInitialized = true;
            }
            catch (Exception ex)
            {
                return ModPublishResult.Fail("初始化 Steam 回调分发失败：" + ex.Message);
            }
        }

        var deadline = DateTime.UtcNow + timeout;
        ulong? publishedFileId = request.PublishedFileId is > 0 ? request.PublishedFileId : null;

        if (publishedFileId is null)
        {
            _createResult = null;
            _callbackError = null;
            progress?.Invoke(new ModPublishProgress("creating", 0, 0, 0));

            ulong call;
            try
            {
                call = _createItem!(_ugc, checked((uint)request.AppId), 0);
            }
            catch (Exception ex)
            {
                return ModPublishResult.Fail("创建工坊条目异常：" + ex.Message);
            }

            if (call == 0)
            {
                return ModPublishResult.Fail("Steam 未返回创建条目的调用句柄");
            }

            while (DateTime.UtcNow < deadline)
            {
                PumpManualCallbacks();
                if (_callbackError is not null)
                {
                    return ModPublishResult.Fail(_callbackError);
                }

                if (_createResult is { } created)
                {
                    if (created.NeedsLegalAgreement)
                    {
                        return ModPublishResult.Fail(
                            "需要先在 Steam 接受创意工坊法律协议",
                            created.PublishedFileId == 0 ? null : created.PublishedFileId,
                            true);
                    }

                    if (created.Result != ResultOk || created.PublishedFileId == 0)
                    {
                        return ModPublishResult.Fail("创建工坊条目失败（EResult=" + created.Result + "）");
                    }

                    publishedFileId = created.PublishedFileId;
                    break;
                }

                Thread.Sleep(100);
            }

            if (publishedFileId is null)
            {
                return ModPublishResult.Fail("创建工坊条目超时");
            }
        }

        // 长度校验放在开始更新之前：Steam 的标题 / 描述是定长缓冲，超长会被 SetItemTitle
        // 静默截断或直接返回 false，用户看到的只是「设置标题失败」，查不出原因。
        if (!ModPublishLimits.TryValidateTitle(request.Title, out var titleLimitError))
        {
            return ModPublishResult.Fail(titleLimitError!, publishedFileId);
        }

        if (!ModPublishLimits.TryValidateDescription(request.Description, out var descriptionLimitError))
        {
            return ModPublishResult.Fail(descriptionLimitError!, publishedFileId);
        }

        ulong handle;
        try
        {
            handle = _startItemUpdate!(_ugc, checked((uint)request.AppId), publishedFileId.Value);
        }
        catch (Exception ex)
        {
            return ModPublishResult.Fail("开始更新工坊条目异常：" + ex.Message);
        }

        if (handle == 0)
        {
            return ModPublishResult.Fail("无法开始更新工坊条目（句柄为空）");
        }

        try
        {
            if (!_setItemTitle!(_ugc, handle, request.Title))
            {
                return ModPublishResult.Fail("设置工坊标题失败", publishedFileId);
            }

            if (!_setItemDescription!(_ugc, handle, request.Description ?? string.Empty))
            {
                return ModPublishResult.Fail("设置工坊简介失败", publishedFileId);
            }

            if (!_setItemContent!(_ugc, handle, request.ContentPath))
            {
                return ModPublishResult.Fail("设置工坊内容目录失败：" + request.ContentPath, publishedFileId);
            }

            if (!string.IsNullOrWhiteSpace(request.PreviewPath) && File.Exists(request.PreviewPath))
            {
                if (!_setItemPreview!(_ugc, handle, request.PreviewPath!))
                {
                    return ModPublishResult.Fail("设置工坊封面失败：" + request.PreviewPath, publishedFileId);
                }
            }

            if (request.Tags is { Count: > 0 } && !SetTags(handle, request.Tags!))
            {
                return ModPublishResult.Fail("设置工坊标签失败", publishedFileId);
            }

            var visibility = (int)request.Visibility;
            if (!_setItemVisibility!(_ugc, handle, visibility))
            {
                return ModPublishResult.Fail("设置工坊可见性失败", publishedFileId);
            }

            if (!EnsureExtrasBindings(
                    needPreview: HasExtras(request),
                    needDescriptors: request.ContentDescriptors is { Count: > 0 },
                    needLanguage: request.Localizations is { Count: > 0 }))
            {
                return ModPublishResult.Fail("steam_api64.dll 缺少设置描述词 / 附加预览 / 多语言所需的导出", publishedFileId);
            }

            ApplyContentDescriptors(handle, request.ContentDescriptors);

            var previewError = ApplyExtraPreviews(handle, request.PreviewPaths, request.PreviewVideoIds);
            if (previewError is not null)
            {
                return ModPublishResult.Fail(previewError, publishedFileId);
            }
        }
        catch (Exception ex)
        {
            return ModPublishResult.Fail("设置工坊元数据异常：" + ex.Message, publishedFileId);
        }

        _submitResult = null;
        _callbackError = null;
        ulong submitCall;
        try
        {
            submitCall = _submitItemUpdate!(_ugc, handle, request.ChangeNote ?? "Initial upload");
        }
        catch (Exception ex)
        {
            return ModPublishResult.Fail("提交工坊更新异常：" + ex.Message, publishedFileId);
        }

        if (submitCall == 0)
        {
            return ModPublishResult.Fail("Steam 未返回上传调用句柄", publishedFileId);
        }

        while (DateTime.UtcNow < deadline)
        {
            PumpManualCallbacks();
            if (_callbackError is not null)
            {
                return ModPublishResult.Fail(_callbackError, publishedFileId);
            }

            try
            {
                var status = _getItemUpdateProgress!(_ugc, handle, out var uploaded, out var total);
                var percent = total > 0 ? (int)Math.Min(100, uploaded * 100 / total) : 0;
                progress?.Invoke(new ModPublishProgress(MapStage(status), percent, uploaded, total));
            }
            catch (Exception)
            {
                // 进度读取失败不影响等待最终回调。
            }

            if (_submitResult is { } submitted)
            {
                if (submitted.NeedsLegalAgreement)
                {
                    return ModPublishResult.Fail(
                        "需要先在 Steam 接受创意工坊法律协议",
                        publishedFileId,
                        true);
                }

                if (submitted.Result != ResultOk)
                {
                    return ModPublishResult.Fail("上传工坊条目失败（EResult=" + submitted.Result + "）", publishedFileId);
                }

                progress?.Invoke(new ModPublishProgress("done", 100, 0, 0));
                var localizedError = SubmitLocalizations(publishedFileId.Value, request, progress, timeout);
                return localizedError is null
                    ? new ModPublishResult(true, publishedFileId, false, null)
                    : new ModPublishResult(true, publishedFileId, false, localizedError);
            }

            Thread.Sleep(100);
        }

        return ModPublishResult.Fail("上传工坊条目超时", publishedFileId);
    }

    /// <summary>这只请求是否带了附加预览（图片或视频）。</summary>
    private static bool HasExtras(ModPublishRequest request) =>
        request.PreviewPaths is { Count: > 0 } || request.PreviewVideoIds is { Count: > 0 };

    /// <summary>
    /// 设置内容描述词。
    /// <para>
    /// 描述词是封闭枚举（1-5），所以先对全部 id 调一次 remove 再 add 目标集合：
    /// 这样「上次勾了 2 和 4、这次只留 3」不会残留旧勾选，天然幂等。
    /// </para>
    /// </summary>
    private void ApplyContentDescriptors(ulong handle, IReadOnlyList<int>? descriptors)
    {
        for (var id = ContentDescriptorFirst; id <= ContentDescriptorLast; id++)
        {
            try
            {
                _removeContentDescriptor!(_ugc, handle, id);
            }
            catch (Exception)
            {
                // 清空失败不影响后面的设置；最坏是残留一项旧勾选。
            }
        }

        if (descriptors is null || descriptors.Count == 0)
        {
            return;
        }

        foreach (var id in descriptors)
        {
            if (id < ContentDescriptorFirst || id > ContentDescriptorLast)
            {
                continue;
            }

            try
            {
                _addContentDescriptor!(_ugc, handle, id);
            }
            catch (Exception)
            {
                // 单个描述词设不上不该让整个上传失败 —— 它是标注，不是内容。
            }
        }
    }

    /// <summary>
    /// 重设附加预览：先清空已有预览，再按顺序加图片与视频。
    /// <para>
    /// 必须放在主封面 <c>SetItemPreview</c> 之后调用 —— 这里删除的是「附加预览」列表，
    /// 但如果 Steam 的行为是把主封面也算进索引，主图会被一起删掉（首次真机手测要核对）。
    /// </para>
    /// </summary>
    /// <returns>失败原因；成功返回 null。</returns>
    private string? ApplyExtraPreviews(
        ulong handle,
        IReadOnlyList<string>? previewPaths,
        IReadOnlyList<string>? videoIds)
    {
        var files = previewPaths ?? [];
        var videos = videoIds ?? [];
        if (files.Count == 0 && videos.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < MaxPreviewRemoval; i++)
        {
            bool removed;
            try
            {
                removed = _removeItemPreview!(_ugc, handle, 0);
            }
            catch (Exception)
            {
                break;
            }

            if (!removed)
            {
                break;
            }
        }

        var added = 0;
        foreach (var path in files)
        {
            if (added >= ModPublishLimits.MaxAdditionalPreviews)
            {
                break;
            }

            if (!File.Exists(path))
            {
                return "找不到预览图：" + path;
            }

            if (!ModPublishLimits.IsPreviewFileAcceptable(new FileInfo(path).Length, out var sizeError))
            {
                return sizeError + "：" + Path.GetFileName(path);
            }

            if (!_addItemPreviewFile!(_ugc, handle, path, PreviewTypeImage))
            {
                return "添加预览图失败：" + Path.GetFileName(path);
            }

            added++;
        }

        foreach (var videoId in videos)
        {
            if (added >= ModPublishLimits.MaxAdditionalPreviews)
            {
                break;
            }

            if (!_addItemPreviewVideo!(_ugc, handle, videoId))
            {
                return "添加视频预览失败：" + videoId;
            }

            added++;
        }

        return null;
    }

    /// <summary>
    /// 主提交成功之后，按语言逐个提交标题与描述。
    /// <para>
    /// **绝不**在这里设置内容目录、封面、可见性、标签或描述词：那些字段是语言无关的，
    /// 重设它们等于把整个 mod 内容按语言再传一遍，既慢又可能被 Steam 拒绝。
    /// </para>
    /// </summary>
    /// <returns>null 表示全部成功；否则是「部分或全部语言未写入」的说明。</returns>
    private string? SubmitLocalizations(
        ulong publishedFileId,
        ModPublishRequest request,
        Action<ModPublishProgress>? progress,
        TimeSpan timeout)
    {
        if (request.Localizations is not { Count: > 0 } all)
        {
            return null;
        }

        var items = all.Take(ModPublishLimits.MaxLocalizations).ToArray();
        var failures = new List<string>();
        for (var i = 0; i < items.Length; i++)
        {
            var text = items[i];
            progress?.Invoke(new ModPublishProgress(
                LocalizingStage,
                (int)((i * 100L) / items.Length),
                0,
                0));

            var error = SubmitLocalizationCore(publishedFileId, request.AppId, text, timeout);
            if (error is not null)
            {
                failures.Add(text.Language + "：" + error);
            }
        }

        if (failures.Count == 0)
        {
            return null;
        }

        var detail = string.Join("；", failures);
        return failures.Count == items.Length
            ? "多语言标题全部提交失败（" + detail + "）"
            : "有 " + failures.Count + " 种语言未写入（" + detail + "）";
    }

    private string? SubmitLocalizationCore(
        ulong publishedFileId,
        int appId,
        ModLocalizedText text,
        TimeSpan timeout)
    {
        if (!ModPublishLimits.TryValidateTitle(text.Title, out var titleLimitError))
        {
            return titleLimitError;
        }

        if (!ModPublishLimits.TryValidateDescription(text.Description, out var descriptionLimitError))
        {
            return descriptionLimitError;
        }

        ulong handle;
        try
        {
            handle = _startItemUpdate!(_ugc, checked((uint)appId), publishedFileId);
        }
        catch (Exception ex)
        {
            return "开始更新异常：" + ex.Message;
        }

        if (handle == 0)
        {
            return "无法开始更新（句柄为空）";
        }

        try
        {
            if (!_setItemUpdateLanguage!(_ugc, handle, text.Language))
            {
                return "设置语言失败";
            }

            if (!_setItemTitle!(_ugc, handle, text.Title))
            {
                return "设置标题失败";
            }

            if (!_setItemDescription!(_ugc, handle, text.Description ?? string.Empty))
            {
                return "设置简介失败";
            }
        }
        catch (Exception ex)
        {
            return "设置语言文本异常：" + ex.Message;
        }

        _submitResult = null;
        _callbackError = null;
        ulong call;
        try
        {
            call = _submitItemUpdate!(_ugc, handle, "Update localized title");
        }
        catch (Exception ex)
        {
            return "提交异常：" + ex.Message;
        }

        if (call == 0)
        {
            return "Steam 未返回上传调用句柄";
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            PumpManualCallbacks();
            if (_callbackError is not null)
            {
                return _callbackError;
            }

            if (_submitResult is { } submitted)
            {
                if (submitted.NeedsLegalAgreement)
                {
                    return "需要先在 Steam 接受创意工坊法律协议";
                }

                return submitted.Result == ResultOk ? null : "EResult=" + submitted.Result;
            }

            Thread.Sleep(100);
        }

        return "提交超时";
    }

    /// <summary>
    /// 列出当前账号在本游戏下**自己发布**的工坊条目。
    /// <para>
    /// 走 <c>EnumerateUserPublishedFiles</c>：每页 50 条，按 <c>unStartIndex</c> 翻页，
    /// 直到返回不足一页、拿满 <c>m_nTotalResultCount</c>、或撞上 <see cref="SteamHostMineResult.MaxItems"/>。
    /// **未公开（仅好友 / 私密 / 未列出）的条目同样在列表里** —— 这是 Web API 对匿名调用方回
    /// <c>EResult 9</c> 时唯一还能看见自己条目的途径。
    /// </para>
    /// </summary>
    /// <param name="timeout">整批（所有页）共用的等待上限。</param>
    /// <returns>自己的条目 id 列表；失败时 <c>Ok</c> 为 false 且带中文原因。</returns>
    public SteamHostMineResult ReadOwnPublishedItems(TimeSpan timeout)
    {
        var error = EnsureInitialized();
        if (error is not null)
        {
            return SteamHostMineResult.Fail(error);
        }

        if (!EnsureRemoteStorageBindings())
        {
            return SteamHostMineResult.Fail("steam_api64.dll 缺少查询自己创意工坊条目所需的 Steamworks 导出（EnumerateUserPublishedFiles 等）");
        }

        var dispatchError = EnsureManualDispatch();
        if (dispatchError is not null)
        {
            return SteamHostMineResult.Fail(dispatchError);
        }

        var deadline = DateTime.UtcNow + timeout;
        var ids = new List<ulong>();
        var seen = new HashSet<ulong>();
        var total = -1;
        var truncated = false;

        for (var page = 0; page < MaxOwnPages; page++)
        {
            _ownEnumerateResult = null;
            _callbackError = null;

            ulong call;
            try
            {
                call = _rsEnumUserPublishedFiles!(_remoteStorage, checked((uint)(page * OwnPageSize)));
            }
            catch (Exception ex)
            {
                return SteamHostMineResult.Fail("列出自己发布的工坊条目异常：" + ex.Message);
            }

            if (call == 0)
            {
                return SteamHostMineResult.Fail("Steam 未返回列举条目的调用句柄");
            }

            while (DateTime.UtcNow < deadline && _ownEnumerateResult is null)
            {
                PumpManualCallbacks();
                if (_callbackError is not null)
                {
                    return SteamHostMineResult.Fail(_callbackError);
                }

                Thread.Sleep(50);
            }

            if (_ownEnumerateResult is not { } pageResult)
            {
                return SteamHostMineResult.Fail("列出自己在工坊发布的条目超时");
            }

            if (pageResult.Result != ResultOk)
            {
                return SteamHostMineResult.Fail("列出自己在工坊发布的条目失败（EResult=" + pageResult.Result + "）");
            }

            total = pageResult.TotalResultCount;
            var returned = Math.Clamp(pageResult.ResultsReturned, 0, OwnPageSize);
            for (var index = 0; index < returned; index++)
            {
                var id = pageResult.Ids[index];
                if (id != 0 && seen.Add(id))
                {
                    ids.Add(id);
                }
            }

            if (returned < OwnPageSize || (total > 0 && ids.Count >= total))
            {
                break;
            }

            if (ids.Count >= SteamHostMineResult.MaxItems)
            {
                truncated = true;
                break;
            }
        }

        return new SteamHostMineResult(true, ids, total, truncated, null, _initialized);
    }

    /// <summary>
    /// 按 id 读**自己**某个工坊条目的详情，含 <b>Steam 侧真实可见性</b>。
    /// <para>未公开条目也读得到（鉴权版 <c>GetPublishedFileDetails</c>），这正是判断
    /// 「用户以为没发布成功、其实是设成了私密」的依据。</para>
    /// </summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="timeout">等待 Steam 返回详情的上限。</param>
    /// <returns>详情结果；<c>EResult 9</c> 会翻成中文原因。</returns>
    public SteamHostMineItemResult ReadOwnPublishedItem(ulong publishedFileId, TimeSpan timeout)
    {
        if (publishedFileId == 0)
        {
            return SteamHostMineItemResult.Fail(0, "工坊条目 id 必须大于 0");
        }

        var error = EnsureInitialized();
        if (error is not null)
        {
            return SteamHostMineItemResult.Fail(publishedFileId, error);
        }

        if (!EnsureRemoteStorageBindings())
        {
            return SteamHostMineItemResult.Fail(publishedFileId, "steam_api64.dll 缺少查询自己创意工坊条目所需的 Steamworks 导出（GetPublishedFileDetails 等）");
        }

        var dispatchError = EnsureManualDispatch();
        if (dispatchError is not null)
        {
            return SteamHostMineItemResult.Fail(publishedFileId, dispatchError);
        }

        _ownDetailResult = null;
        _callbackError = null;

        ulong call;
        try
        {
            call = _rsGetPublishedFileDetails!(_remoteStorage, publishedFileId, 0);
        }
        catch (Exception ex)
        {
            return SteamHostMineItemResult.Fail(publishedFileId, "读取工坊条目异常：" + ex.Message);
        }

        if (call == 0)
        {
            return SteamHostMineItemResult.Fail(publishedFileId, "Steam 未返回读取条目详情的调用句柄");
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && _ownDetailResult is null)
        {
            PumpManualCallbacks();
            if (_callbackError is not null)
            {
                return SteamHostMineItemResult.Fail(publishedFileId, _callbackError);
            }

            Thread.Sleep(50);
        }

        if (_ownDetailResult is not { } detail)
        {
            return SteamHostMineItemResult.Fail(publishedFileId, "读取工坊条目详情超时");
        }

        if (detail.Result == FileNotFoundResult)
        {
            return SteamHostMineItemResult.Fail(publishedFileId, "Steam 说这个条目不存在（EResult=9）");
        }

        if (detail.Result != ResultOk)
        {
            return SteamHostMineItemResult.Fail(publishedFileId, "读取工坊条目失败（EResult=" + detail.Result + "）");
        }

        return new SteamHostMineItemResult(
            true,
            detail.PublishedFileId == 0 ? publishedFileId : detail.PublishedFileId,
            detail.Title ?? string.Empty,
            detail.Description ?? string.Empty,
            SplitTagList(detail.Tags),
            detail.Visibility,
            detail.OwnerSteamId64,
            detail.FileSize,
            detail.TimeCreated,
            detail.TimeUpdated,
            (int)Math.Min(detail.CreatorAppId, int.MaxValue),
            null);
    }

    /// <summary>自检用：枚举结果结构体的托管尺寸（钉住布局，字段或修饰符漂移时立刻红）。</summary>
    internal static int OwnEnumerateResultSize => Marshal.SizeOf<OwnEnumerateFilesResult>();

    /// <summary>自检用：详情结果结构体的托管尺寸（期望 9760，主要是 8000 字节的描述缓冲 + 各定长 char 数组）。</summary>
    internal static int OwnDetailResultSize => Marshal.SizeOf<OwnFileDetailResult>();

    private static IReadOnlyList<string> SplitTagList(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var part in tags.Split(','))
        {
            var tag = part.Trim();
            if (tag.Length > 0)
            {
                values.Add(tag);
            }
        }

        return values;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (_initialized)
            {
                _shutdown?.Invoke();
            }
        }
        catch
        {
            // 宿主马上退出，Shutdown 的异常没有意义。
        }
        finally
        {
            _initialized = false;
            if (_library != IntPtr.Zero)
            {
                try
                {
                    NativeLibrary.Free(_library);
                }
                catch
                {
                    // 同上：进程即将结束。
                }

                _library = IntPtr.Zero;
            }

            if (_previousAppId is not null)
            {
                Environment.SetEnvironmentVariable(AppIdVariable, _previousAppId);
            }
        }
    }

    private string? EnsureInitialized()
    {
        if (_initialized)
        {
            return null;
        }

        if (!File.Exists(_apiFilePath))
        {
            return "找不到 steam_api64.dll：" + _apiFilePath;
        }

        try
        {
            _library = NativeLibrary.Load(_apiFilePath);
        }
        catch (Exception ex)
        {
            return "加载 steam_api64.dll 失败：" + ex.Message;
        }

        _shutdown = Bind<ShutdownFn>("SteamAPI_Shutdown");
        _runCallbacks = Bind<RunCallbacksFn>("SteamAPI_RunCallbacks");
        _steamUgc = BindUgcAccessor();
        _steamRemoteStorage = BindRemoteStorageAccessor();
        _rsEnumUserPublishedFiles = Bind<SteamRemoteStorageEnumFn>("SteamAPI_ISteamRemoteStorage_EnumerateUserPublishedFiles");
        _rsGetPublishedFileDetails = Bind<SteamRemoteStorageDetailFn>("SteamAPI_ISteamRemoteStorage_GetPublishedFileDetails");
        _subscribeItem = Bind<SubscribeItemFn>("SteamAPI_ISteamUGC_SubscribeItem");
        _unsubscribeItem = Bind<UnsubscribeItemFn>("SteamAPI_ISteamUGC_UnsubscribeItem");
        _getItemState = Bind<GetItemStateFn>("SteamAPI_ISteamUGC_GetItemState");
        _createItem = Bind<CreateItemFn>("SteamAPI_ISteamUGC_CreateItem");
        _startItemUpdate = Bind<StartItemUpdateFn>("SteamAPI_ISteamUGC_StartItemUpdate");
        _submitItemUpdate = Bind<SubmitItemUpdateFn>("SteamAPI_ISteamUGC_SubmitItemUpdate");
        _setItemTitle = Bind<SetItemTextFn>("SteamAPI_ISteamUGC_SetItemTitle");
        _setItemDescription = Bind<SetItemTextFn>("SteamAPI_ISteamUGC_SetItemDescription");
        _setItemContent = Bind<SetItemTextFn>("SteamAPI_ISteamUGC_SetItemContent");
        _setItemPreview = Bind<SetItemTextFn>("SteamAPI_ISteamUGC_SetItemPreview");
        _setItemVisibility = Bind<SetItemVisibilityFn>("SteamAPI_ISteamUGC_SetItemVisibility");
        _setItemTags = Bind<SetItemTagsFn>("SteamAPI_ISteamUGC_SetItemTags");
        _setItemUpdateLanguage = Bind<SetItemTextFn>("SteamAPI_ISteamUGC_SetItemUpdateLanguage");
        _addItemPreviewFile = Bind<AddItemPreviewFileFn>("SteamAPI_ISteamUGC_AddItemPreviewFile");
        _addItemPreviewVideo = Bind<AddItemPreviewVideoFn>("SteamAPI_ISteamUGC_AddItemPreviewVideo");
        _removeItemPreview = Bind<RemoveItemPreviewFn>("SteamAPI_ISteamUGC_RemoveItemPreview");
        _addContentDescriptor = Bind<ContentDescriptorFn>("SteamAPI_ISteamUGC_AddContentDescriptor");
        _removeContentDescriptor = Bind<ContentDescriptorFn>("SteamAPI_ISteamUGC_RemoveContentDescriptor");
        _getItemUpdateProgress = Bind<GetItemUpdateProgressFn>("SteamAPI_ISteamUGC_GetItemUpdateProgress");
        _getNumSubscribedItems = Bind<GetNumSubscribedItemsFn>("SteamAPI_ISteamUGC_GetNumSubscribedItems");
        _getSubscribedItems = Bind<GetSubscribedItemsFn>("SteamAPI_ISteamUGC_GetSubscribedItems");
        _getItemInstallInfo = Bind<GetItemInstallInfoFn>("SteamAPI_ISteamUGC_GetItemInstallInfo");
        _getItemDownloadInfo = Bind<GetItemDownloadInfoFn>("SteamAPI_ISteamUGC_GetItemDownloadInfo");
        _downloadItem = Bind<DownloadItemFn>("SteamAPI_ISteamUGC_DownloadItem");
        _setItemsDisabledLocally = Bind<SetItemsDisabledLocallyFn>("SteamAPI_ISteamUGC_SetItemsDisabledLocally");
        _getHSteamPipe = Bind<GetHSteamPipeFn>("SteamAPI_GetHSteamPipe");
        _manualDispatchInit = Bind<ManualDispatchInitFn>("SteamAPI_ManualDispatch_Init");
        _manualDispatchRunFrame = Bind<ManualDispatchRunFrameFn>("SteamAPI_ManualDispatch_RunFrame");
        _manualDispatchGetNextCallback = Bind<ManualDispatchGetNextCallbackFn>("SteamAPI_ManualDispatch_GetNextCallback");
        _manualDispatchFreeLastCallback = Bind<ManualDispatchFreeLastCallbackFn>("SteamAPI_ManualDispatch_FreeLastCallback");
        _manualDispatchGetApiCallResult = Bind<ManualDispatchGetApiCallResultFn>("SteamAPI_ManualDispatch_GetAPICallResult");

        if (_shutdown is null || _getItemState is null)
        {
            return "steam_api64.dll 缺少必要的 Steamworks 导出（Shutdown/GetItemState）";
        }

        _previousAppId = Environment.GetEnvironmentVariable(AppIdVariable);
        Environment.SetEnvironmentVariable(AppIdVariable, _appId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var buffer = new byte[InitFlatBufferLength];
        var initFlat = Bind<InitFlatFn>("SteamAPI_InitFlat");
        var initialized = false;
        if (initFlat is not null && initFlat(buffer) == 0)
        {
            initialized = true;
        }
        else
        {
            var plain = Bind<InitFn>("SteamAPI_Init");
            if (plain is not null && plain())
            {
                initialized = true;
            }
        }

        if (!initialized)
        {
            var message = Encoding.UTF8.GetString(buffer).TrimEnd('\0', ' ');
            return message.Length > 0 ? "SteamAPI_Init 失败：" + message : "SteamAPI_Init 失败（Steam 未运行或未登录）";
        }

        _initialized = true;
        try
        {
            _ugc = _steamUgc?.Invoke() ?? IntPtr.Zero;

            try
            {
                _remoteStorage = _steamRemoteStorage?.Invoke() ?? IntPtr.Zero;
            }
            catch
            {
                _remoteStorage = IntPtr.Zero;
            }
        }
        catch
        {
            _ugc = IntPtr.Zero;
        }

        return null;
    }

    private SteamUgcFn? BindUgcAccessor()
    {
        foreach (var version in new[] { 21, 20, 18, 17, 16, 15, 14, 13, 12 })
        {
            var accessor = Bind<SteamUgcFn>("SteamAPI_SteamUGC_v" + version.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            if (accessor is not null)
            {
                return accessor;
            }
        }

        return null;
    }

    private SteamRemoteStorageFn? BindRemoteStorageAccessor()
    {
        // 实测游戏自带的 dll 只导出 v014；仍照 UGC 的写法往上探，
        // 免得换一款游戏（或 Steam 更新）多了新版本反而绑不上。
        foreach (var version in new[] { 16, 15, 14, 13 })
        {
            var accessor = Bind<SteamRemoteStorageFn>("SteamAPI_SteamRemoteStorage_v" + version.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            if (accessor is not null)
            {
                return accessor;
            }
        }

        return null;
    }

    private bool EnsurePublishBindings() =>
        _ugc != IntPtr.Zero
        && _createItem is not null
        && _startItemUpdate is not null
        && _submitItemUpdate is not null
        && _setItemTitle is not null
        && _setItemDescription is not null
        && _setItemContent is not null
        && _setItemPreview is not null
        && _setItemVisibility is not null
        && _setItemTags is not null
        && _getItemUpdateProgress is not null
        && _getHSteamPipe is not null
        && _manualDispatchInit is not null
        && _manualDispatchRunFrame is not null
        && _manualDispatchGetNextCallback is not null
        && _manualDispatchFreeLastCallback is not null
        && _manualDispatchGetApiCallResult is not null;

    /// <summary>
    /// 扩展能力（内容描述词 / 附加预览 / 多语言）的导出是否齐全。
    /// <para>
    /// 刻意与 <see cref="EnsurePublishBindings"/> 分开：旧版 <c>steam_api64.dll</c> 或裁剪过的
    /// 发行包里可能缺这几个导出，但基础发布仍然可用，不能因为它们把整个上传拦住。
    /// </para>
    /// </summary>
    private bool EnsureExtrasBindings(bool needPreview, bool needDescriptors, bool needLanguage) =>
        _ugc != IntPtr.Zero
        && (!needPreview
            || (_addItemPreviewFile is not null
                && _addItemPreviewVideo is not null
                && _removeItemPreview is not null))
        && (!needDescriptors
            || (_addContentDescriptor is not null && _removeContentDescriptor is not null))
        && (!needLanguage || _setItemUpdateLanguage is not null);

    /// <summary>查「我自己发布的条目」所需的导出是否齐全（含手动回调分发那一套）。</summary>
    private bool EnsureRemoteStorageBindings() =>
        _remoteStorage != IntPtr.Zero
        && _rsEnumUserPublishedFiles is not null
        && _rsGetPublishedFileDetails is not null
        && _getHSteamPipe is not null
        && _manualDispatchInit is not null
        && _manualDispatchRunFrame is not null
        && _manualDispatchGetNextCallback is not null
        && _manualDispatchFreeLastCallback is not null
        && _manualDispatchGetApiCallResult is not null;

    /// <summary>确保手动回调分发已初始化（与 <see cref="Publish"/> 里那段同义）。</summary>
    private string? EnsureManualDispatch()
    {
        if (_manualInitialized)
        {
            return null;
        }

        try
        {
            _manualDispatchInit!();
            _pipe = _getHSteamPipe!();
            _manualInitialized = true;
            return null;
        }
        catch (Exception ex)
        {
            return "初始化 Steam 回调分发失败：" + ex.Message;
        }
    }

    private void PumpCallbacks()
    {
        try
        {
            _runCallbacks?.Invoke();
        }
        catch
        {
            // 回调泵失败不影响状态位轮询。
        }
    }

    private void PumpManualCallbacks()
    {
        if (!_manualInitialized)
        {
            return;
        }

        _manualDispatchRunFrame!(_pipe);
        var message = Marshal.AllocHGlobal(Marshal.SizeOf<SteamCallbackMsg>());
        try
        {
            while (_manualDispatchGetNextCallback!(_pipe, message))
            {
                try
                {
                    var header = Marshal.PtrToStructure<SteamCallbackMsg>(message);
                    if (header.CallbackId != SteamApiCallCompletedId || header.Param == IntPtr.Zero)
                    {
                        continue;
                    }

                    var completed = Marshal.PtrToStructure<SteamApiCallCompleted>(header.Param);
                    var size = completed.Callback switch
                    {
                        CreateItemResultId => Marshal.SizeOf<CreateItemResult>(),
                        SubmitItemUpdateResultId => Marshal.SizeOf<SubmitItemUpdateResult>(),
                        OwnEnumerateResultId => Marshal.SizeOf<OwnEnumerateFilesResult>(),
                        OwnDetailResultId => Marshal.SizeOf<OwnFileDetailResult>(),
                        _ => 0,
                    };

                    if (size == 0)
                    {
                        continue;
                    }
                    var buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (_manualDispatchGetApiCallResult!(
                                _pipe,
                                completed.AsyncCall,
                                buffer,
                                size,
                                completed.Callback,
                                out var ioFailure))
                        {
                            switch (completed.Callback)
                            {
                                case CreateItemResultId:
                                    _createResult = Marshal.PtrToStructure<CreateItemResult>(buffer);
                                    break;
                                case SubmitItemUpdateResultId:
                                    _submitResult = Marshal.PtrToStructure<SubmitItemUpdateResult>(buffer);
                                    break;
                                case OwnEnumerateResultId:
                                    _ownEnumerateResult = Marshal.PtrToStructure<OwnEnumerateFilesResult>(buffer);
                                    break;
                                default:
                                    _ownDetailResult = Marshal.PtrToStructure<OwnFileDetailResult>(buffer);
                                    break;
                            }
                        }
                        else if (ioFailure)
                        {
                            _callbackError = "读取 Steam 回调结果失败（IO 错误）";
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    _manualDispatchFreeLastCallback!(_pipe);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(message);
        }
    }

    private bool SetTags(ulong handle, IReadOnlyList<string> tags)
    {
        var strings = new IntPtr[tags.Count];
        var array = Marshal.AllocHGlobal(IntPtr.Size * tags.Count);
        try
        {
            for (var index = 0; index < tags.Count; index++)
            {
                strings[index] = Marshal.StringToCoTaskMemUTF8(tags[index]);
                Marshal.WriteIntPtr(array, index * IntPtr.Size, strings[index]);
            }

            var nativeArray = new SteamParamStringArray
            {
                Strings = array,
                Count = tags.Count,
            };
            var nativeArrayPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SteamParamStringArray>());
            try
            {
                Marshal.StructureToPtr(nativeArray, nativeArrayPtr, fDeleteOld: false);
                return _setItemTags!(_ugc, handle, nativeArrayPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(nativeArrayPtr);
            }
        }
        finally
        {
            foreach (var pointer in strings)
            {
                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }

            Marshal.FreeHGlobal(array);
        }
    }

    private uint GetItemState(ulong publishedFileId)
    {
        if (_ugc == IntPtr.Zero || _getItemState is null)
        {
            return 0;
        }

        try
        {
            return _getItemState(_ugc, publishedFileId);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 读一条工坊条目的完整状态：位 + 磁盘占用 + 安装目录 + 下载进度。
    /// <para>
    /// 位为 0（Steam 没认出这个条目）时**不能**当成「确定未订阅」—— 那可能只是还没同步。
    /// 所以此时返回 <see cref="WorkshopInstallState.Unknown"/>，交给 acf 回退路径去核对。
    /// </para>
    /// </summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <returns>状态；位为 0 时为 Unknown。</returns>
    private WorkshopInstallState ReadStateDetail(ulong publishedFileId)
    {
        var raw = GetItemState(publishedFileId);
        if (raw == 0)
        {
            return WorkshopInstallState.Unknown;
        }

        ulong? sizeOnDisk = null;
        string? folder = null;
        ulong? downloaded = null;
        ulong? total = null;

        if (_getItemInstallInfo is not null && _ugc != IntPtr.Zero)
        {
            var buffer = new byte[1024];
            try
            {
                if (_getItemInstallInfo(_ugc, publishedFileId, out var size, buffer, (uint)buffer.Length, out _))
                {
                    sizeOnDisk = size;
                    var name = Encoding.UTF8.GetString(buffer);
                    var zero = name.IndexOf('\0');
                    if (zero >= 0)
                    {
                        name = name[..zero];
                    }

                    if (name.Length > 0)
                    {
                        folder = name;
                    }
                }
            }
            catch
            {
                // 目录信息拿不到不影响位标记，只是少一档 UI 文案。
            }
        }

        if (_getItemDownloadInfo is not null && _ugc != IntPtr.Zero)
        {
            try
            {
                if (_getItemDownloadInfo(_ugc, publishedFileId, out var done, out var size) && size > 0)
                {
                    downloaded = done;
                    total = size;
                }
            }
            catch
            {
                // 同上。
            }
        }

        return WorkshopItemStateBits.ToInstallState(raw, sizeOnDisk, folder, downloaded, total);
    }

    /// <summary>
    /// 订阅确认专用：<c>raw==0</c> 在这里是**合法结论**（= 未订阅），不能按「状态未知」处理，
    /// 否则「取消订阅」永远等不到翻转。
    /// </summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <returns>状态；位为 0 时是「确定未订阅」。</returns>
    private WorkshopInstallState ReadStateForConfirm(ulong publishedFileId)
    {
        var raw = GetItemState(publishedFileId);
        return raw == 0
            ? new WorkshopInstallState(IsSubscribed: false, IsInstalled: false, IsDownloading: false, AcfPath: null)
            : ReadStateDetail(publishedFileId);
    }

    /// <summary>写动作后轮询，直到该条目出现/消失对应位；超时返回最后一次读到的状态。</summary>
    private WorkshopInstallState WaitForState(ulong publishedFileId, string op, DateTime deadline)
    {
        var state = ReadStateDetail(publishedFileId);
        while (DateTime.UtcNow < deadline)
        {
            if (IsSettled(state, op))
            {
                return state;
            }

            PumpCallbacks();
            Thread.Sleep(200);
            state = ReadStateDetail(publishedFileId);
        }

        return state;
    }

    /// <summary>某个写动作是否已经「有结论」：要么位对了，要么根本读不到（别在原地空等满整个超时）。</summary>
    private static bool IsSettled(WorkshopInstallState state, string op)
    {
        if (!state.IsKnown)
        {
            return true;
        }

        return op switch
        {
            WorkshopBatchOps.Disable => state.IsDisabledLocally == true,
            WorkshopBatchOps.Enable => state.IsDisabledLocally == false,
            WorkshopBatchOps.Download => state.IsDownloading == true || state.IsInstalled == true,
            _ => true,
        };
    }

    private uint SafeGetNumSubscribedItems()
    {
        if (_ugc == IntPtr.Zero || _getNumSubscribedItems is null)
        {
            return 0;
        }

        try
        {
            return _getNumSubscribedItems(_ugc, false);
        }
        catch
        {
            return 0;
        }
    }

    private bool EnsureSubscriptionBindings() =>
        _ugc != IntPtr.Zero
        && _getNumSubscribedItems is not null
        && _getSubscribedItems is not null;

    private static WorkshopInstallState MapState(uint raw) => WorkshopItemStateBits.ToInstallState(raw);

    private static string MapStage(int status) => status switch
    {
        1 => "preparing",
        2 => "preparing",
        3 => "uploading",
        4 => "preview",
        5 => "committing",
        _ => "uploading",
    };

    /// <summary>取一个导出并绑定成委托；不存在返回 null。</summary>
    private T? Bind<T>(string exportName)
        where T : Delegate
    {
        if (_library == IntPtr.Zero || !NativeLibrary.TryGetExport(_library, exportName, out var address) || address == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        catch
        {
            return null;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitFlatFn(byte[] errorMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool InitFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ShutdownFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RunCallbacksFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamUgcFn();

    private delegate IntPtr SteamRemoteStorageFn();

    private delegate ulong SteamRemoteStorageEnumFn(IntPtr remoteStorage, uint startIndex);

    private delegate ulong SteamRemoteStorageDetailFn(IntPtr remoteStorage, ulong publishedFileId, uint maxSecondsOld);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong SubscribeItemFn(IntPtr ugc, ulong publishedFileId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong UnsubscribeItemFn(IntPtr ugc, ulong publishedFileId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetItemStateFn(IntPtr ugc, ulong publishedFileId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong CreateItemFn(IntPtr ugc, uint appId, uint fileType);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong StartItemUpdateFn(IntPtr ugc, uint appId, ulong publishedFileId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong SubmitItemUpdateFn(IntPtr ugc, ulong handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string changeNote);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetItemTextFn(IntPtr ugc, ulong handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetItemVisibilityFn(IntPtr ugc, ulong handle, int visibility);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetItemTagsFn(IntPtr ugc, ulong handle, IntPtr tags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool AddItemPreviewFileFn(
        IntPtr ugc,
        ulong handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string previewFile,
        int previewType);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool AddItemPreviewVideoFn(
        IntPtr ugc,
        ulong handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string videoId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool RemoveItemPreviewFn(IntPtr ugc, ulong handle, uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ContentDescriptorFn(IntPtr ugc, ulong handle, int descriptorId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetItemUpdateProgressFn(IntPtr ugc, ulong handle, out ulong uploaded, out ulong total);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetNumSubscribedItemsFn(IntPtr ugc, [MarshalAs(UnmanagedType.I1)] bool includeLocallyDisabled);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetSubscribedItemsFn(
        IntPtr ugc,
        [Out] ulong[] publishedFileIds,
        uint maxEntries,
        [MarshalAs(UnmanagedType.I1)] bool includeLocallyDisabled);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetItemInstallInfoFn(
        IntPtr ugc,
        ulong publishedFileId,
        out ulong sizeOnDisk,
        byte[] folder,
        uint folderBufferSize,
        out uint timeStamp);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetItemDownloadInfoFn(
        IntPtr ugc,
        ulong publishedFileId,
        out ulong bytesDownloaded,
        out ulong bytesTotal);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool DownloadItemFn(IntPtr ugc, ulong publishedFileId, [MarshalAs(UnmanagedType.I1)] bool highPriority);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetItemsDisabledLocallyFn(
        IntPtr ugc,
        [In] ulong[] publishedFileIds,
        uint count,
        [MarshalAs(UnmanagedType.I1)] bool disabledLocally);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetHSteamPipeFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ManualDispatchInitFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ManualDispatchRunFrameFn(int pipe);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ManualDispatchGetNextCallbackFn(int pipe, IntPtr message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ManualDispatchFreeLastCallbackFn(int pipe);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ManualDispatchGetApiCallResultFn(
        int pipe,
        ulong apiCall,
        IntPtr callback,
        int callbackSize,
        int expectedCallback,
        [MarshalAs(UnmanagedType.I1)] out bool ioFailure);

    [StructLayout(LayoutKind.Sequential)]
    private struct SteamCallbackMsg
    {
        public int SteamUser;
        public int CallbackId;
        public IntPtr Param;
        public int ParamSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SteamApiCallCompleted
    {
        public ulong AsyncCall;
        public int Callback;
        public uint ParamSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateItemResult
    {
        public int Result;
        public ulong PublishedFileId;

        [MarshalAs(UnmanagedType.I1)]
        public bool NeedsLegalAgreement;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SubmitItemUpdateResult
    {
        public int Result;

        [MarshalAs(UnmanagedType.I1)]
        public bool NeedsLegalAgreement;

        public ulong PublishedFileId;
    }

    /// <summary>
    /// <c>RemoteStorageEnumerateUserPublishedFilesResult_t</c>（回调 1312）。
    /// <para>
    /// 布局照 <c>sdk/public/steam/isteamremotestorage.h:386-393</c>：3 个 int32 + 50 个
    /// <c>PublishedFileId_t</c>。x64 下 <c>Marshal.SizeOf</c> = 416（原生 412，尾部按 8 对齐补齐），
    /// 传大一点给 <c>GetAPICallResult</c> 是安全的。
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct OwnEnumerateFilesResult
    {
        public int Result;
        public int ResultsReturned;
        public int TotalResultCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = OwnPageSize)]
        public ulong[] Ids;
    }

    /// <summary>
    /// <c>RemoteStorageGetPublishedFileDetailsResult_t</c>（回调 1318）。
    /// <para>
    /// 字段顺序照 <c>sdk/public/steam/isteamremotestorage.h:467-497</c>，定长缓冲的长度取自
    /// <c>isteamremotestorage.h:52-58</c>（129 / 8000 / 1025 / 260 / 256）。
    /// <b>三个 <c>bool</c> 必须显式标 <see cref="UnmanagedType.I1"/></b>：C# 默认把 <c>bool</c> 按 4 字节
    /// 封送，而 SDK 里是 1 字节，不标就会把后面的 <c>char[1025]</c> 整体错位。
    /// 期望 <c>Marshal.SizeOf</c> = 9760（起头的 4 字节 int 之后按 8 对齐；描述缓冲 8000 字节 + 其余定长 char 数组）。
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct OwnFileDetailResult
    {
        public int Result;
        public ulong PublishedFileId;
        public uint CreatorAppId;
        public uint ConsumerAppId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
        public string Title;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8000)]
        public string Description;

        public ulong FileHandle;
        public ulong PreviewFileHandle;
        public ulong OwnerSteamId64;
        public uint TimeCreated;
        public uint TimeUpdated;
        public int Visibility;

        [MarshalAs(UnmanagedType.I1)]
        public bool Banned;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1025)]
        public string Tags;

        [MarshalAs(UnmanagedType.I1)]
        public bool TagsTruncated;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;

        public int FileSize;
        public int PreviewFileSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Url;

        public int FileType;

        [MarshalAs(UnmanagedType.I1)]
        public bool AcceptedForUse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SteamParamStringArray
    {
        public IntPtr Strings;
        public int Count;
    }
}
