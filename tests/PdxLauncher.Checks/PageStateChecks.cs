namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

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

        ChecksContext.Check("页面状态：初始没有 Mod 库快照", PageStateStore.ModLibrary is null);
        ChecksContext.Check("页面状态：初始没有 Mod 创建页快照", PageStateStore.ModStudio is null);

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

        // 清掉，避免污染其它检查或同进程内的后续运行。
        PageStateStore.ModLibrary = null;
        PageStateStore.ModStudio = null;
        ChecksContext.Check(
            "页面状态：可以清空",
            PageStateStore.ModLibrary is null && PageStateStore.ModStudio is null);

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
