namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// 页面状态快照（<see cref="PageStateStore"/> 与三个 record）的自检：
/// 字段能原样存取、record 相等按值比较、默认状态是「没有快照」。
/// <para>
/// 这些类型只在会话内使用，出错的后果是「回到页面搜索词/滚动位置丢了」或「表单草稿被悄悄改了」，
/// 编译器看不出来，所以在这里把字段口径钉住。
/// </para>
/// </summary>
public static class PageStateChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        PageStateStore.ModLibrary = null;
        PageStateStore.ModStudio = null;
        PageStateStore.AddMods = null;

        ChecksContext.Check("页面状态：初始没有 Mod 库快照", PageStateStore.ModLibrary is null);
        ChecksContext.Check("页面状态：初始没有 Mod 创建页快照", PageStateStore.ModStudio is null);
        ChecksContext.Check("页面状态：初始没有添加模组页快照", PageStateStore.AddMods is null);

        var library = new ModLibraryState(
            GameId: "ck3",
            SearchText: "ui",
            SortMode: "名称",
            SourceFilter: "全部",
            VerticalOffset: 512.5);
        PageStateStore.ModLibrary = library;

        var readBack = PageStateStore.ModLibrary;
        ChecksContext.Check(
            "页面状态：Mod 库快照字段原样存取",
            readBack is not null
                && readBack.GameId == "ck3"
                && readBack.SearchText == "ui"
                && readBack.SortMode == "名称"
                && readBack.SourceFilter == "全部"
                && Math.Abs(readBack.VerticalOffset - 512.5) < 0.001);

        var addMods = new AddModsState(SortMode: "大小（大到小）");
        PageStateStore.AddMods = addMods;
        ChecksContext.Check(
            "页面状态：添加模组页快照字段原样存取",
            PageStateStore.AddMods is not null
                && PageStateStore.AddMods.SortMode == "大小（大到小）");

        var draft = new ModStudioDraft(
            CreateName: "我的模组",
            CreateFolderName: "my_mod",
            CreateVersion: "0.2.0",
            CreateSupportedVersion: "1.16.*",
            CreateTags: "Gameplay",
            CreateDescription: "说明",
            CreatePreviewPath: @"C:\tmp\preview.png",
            UploadTitle: "标题",
            UploadDescription: "描述",
            UploadTags: "Gameplay, UI",
            UploadChangeNote: "修了个 bug",
            UploadPreviewPath: null,
            UploadPublic: true,
            UploadTarget: "Steam",
            UploadModId: "2216623537",
            UploadDirectory: @"C:\games\mods\my_mod",
            UploadVisibility: "Public",
            UploadDescriptorAdult: false,
            UploadDescriptorViolence: true);

        var studio = new ModStudioState(
            GameId: "ck3",
            SelectedPath: @"C:\games\mods\my_mod",
            Pane: "Create",
            DetailOffset: 0,
            CreateOffset: 120,
            UploadOffset: 0,
            Draft: draft);
        PageStateStore.ModStudio = studio;

        var studioBack = PageStateStore.ModStudio;
        ChecksContext.Check(
            "页面状态：Mod 创建页快照字段原样存取",
            studioBack is not null
                && studioBack.GameId == "ck3"
                && studioBack.SelectedPath == @"C:\games\mods\my_mod"
                && studioBack.Pane == "Create"
                && Math.Abs(studioBack.CreateOffset - 120) < 0.001);

        ChecksContext.Check(
            "页面状态：草稿按值比较（同值相等、改一个字段就不等）",
            studioBack is not null
                && studioBack.Draft == draft
                && studioBack.Draft != draft with { UploadPublic = false });

        ChecksContext.Check(
            "页面状态：草稿保留空预览图",
            studioBack is not null && studioBack.Draft.UploadPreviewPath is null);

        // 落盘形态：快照能整份序列化再读回来，坏 JSON 当没有。
        var snapshotJson = PageStateSnapshot.Serialize(new PageStateSnapshot(library, studio, addMods));
        var restored = PageStateSnapshot.Deserialize(snapshotJson);
        ChecksContext.Check(
            "页面状态：快照序列化往返不丢字段",
            restored is not null
                && restored.ModLibrary == library
                && restored.ModStudio == studio
                && restored.AddMods == addMods);
        ChecksContext.Check(
            "页面状态：旧快照没有添加模组页字段时按 null 处理",
            PageStateSnapshot.Deserialize("{\"ModLibrary\":null,\"ModStudio\":null}") is { AddMods: null });
        ChecksContext.Check(
            "页面状态：坏 JSON 当没有",
            PageStateSnapshot.Deserialize("not json") is null
                && PageStateSnapshot.Deserialize(null) is null
                && PageStateSnapshot.Deserialize(string.Empty) is null);

        // 持久化：Persist 写进 documents，Attach 再读回来。
        var documents = new FakeStore();
        PageStateStore.Persist();
        ChecksContext.Check("页面状态：未 Attach 时 Persist 不写库", !documents.Exists(JsonDocumentKeys.UiPageState));

        PageStateStore.Attach(documents, new SilentLogger());
        PageStateStore.Persist();
        ChecksContext.Check(
            "页面状态：Persist 写进 ui:page-state",
            documents.TryRead(JsonDocumentKeys.UiPageState, out var persisted)
                && persisted is not null
                && persisted.Contains("ck3", StringComparison.Ordinal));

        PageStateStore.ModLibrary = null;
        PageStateStore.ModStudio = null;
        PageStateStore.AddMods = null;
        PageStateStore.Attach(documents, new SilentLogger());
        ChecksContext.Check(
            "页面状态：重启后 Attach 能恢复三份快照",
            PageStateStore.ModLibrary == library
                && PageStateStore.ModStudio == studio
                && PageStateStore.AddMods == addMods);

        // 清掉，避免污染其它检查或同进程内的后续运行。
        PageStateStore.ModLibrary = null;
        PageStateStore.ModStudio = null;
        PageStateStore.AddMods = null;
        ChecksContext.Check(
            "页面状态：可以清空",
            PageStateStore.ModLibrary is null
                && PageStateStore.ModStudio is null
                && PageStateStore.AddMods is null);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private sealed class FakeStore : IJsonDocumentStore
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

        public IReadOnlyList<string> List(string prefix) =>
            _documents.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
    }

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
