namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Storage;

/// <summary>
/// 七类联网缓存的统一目录：设置页与「性能与日志」页共用同一份行定义与清除/统计逻辑，
/// 避免两个页面各写一套 lambda 后行为走偏。
/// 只依赖 AppServices 静态服务，不含任何 Microsoft.UI.Xaml 类型。
/// </summary>
internal static class CacheRowCatalog
{
    /// <summary>按界面顺序建出七行缓存卡片（标题/说明键由各页面自行翻译）。</summary>
    public static List<CacheRowViewModel> Create() => new()
    {
        new(
            CacheRowViewModel.ThemeKey,
            Keys.SettingsCacheRowThemeTitle,
            Keys.SettingsCacheRowThemeDesc,
            JsonDocumentKeys.CachePrefix + "theme-assets:*",
            () =>
            {
                // 主题索引按游戏各一行，逐键删；图片仍是 theme-cache 下的素材文件。
                foreach (var key in AppServices.Documents.List(JsonDocumentKeys.CachePrefix + "theme-assets:"))
                {
                    AppServices.Documents.Delete(key);
                }

                CacheStorage.Clear(AppServices.FileSystem, AppServices.ThemeCacheRoot);
            },
            JsonDocumentKeys.CachePrefix + "theme-assets:",
            AppServices.ThemeCacheRoot),
        new(
            CacheRowViewModel.NewsKey,
            Keys.SettingsCacheRowNewsTitle,
            Keys.SettingsCacheRowNewsDesc,
            JsonDocumentKeys.NewsCards,
            AppServices.NewsFeed.ClearCache,
            JsonDocumentKeys.NewsCards,
            AppServices.NewsCacheRoot),
        new(
            CacheRowViewModel.ModThumbnailsKey,
            Keys.SettingsCacheRowModThumbnailsTitle,
            Keys.SettingsCacheRowModThumbnailsDesc,
            JsonDocumentKeys.ModThumbnails,
            () => AppServices.ModThumbnails.Cache.Clear(),
            JsonDocumentKeys.ModThumbnails,
            AppServices.ModThumbnailCacheRoot),
        new(
            CacheRowViewModel.DlcDescriptionsKey,
            Keys.SettingsCacheRowDlcDescriptionsTitle,
            Keys.SettingsCacheRowDlcDescriptionsDesc,
            JsonDocumentKeys.DlcDescriptions,
            AppServices.DlcDescriptions.ClearCache,
            JsonDocumentKeys.DlcDescriptions,
            null),
        new(
            CacheRowViewModel.ImagesKey,
            Keys.SettingsCacheRowImagesTitle,
            Keys.SettingsCacheRowImagesDesc,
            AppServices.ImageArt.CacheDirectory,
            () =>
            {
                CacheStorage.Clear(AppServices.FileSystem, AppServices.ImageArt.CacheDirectory);
                AppServices.ImageArt.Reset();
            }),
        new(
            CacheRowViewModel.WorkshopKey,
            Keys.SettingsCacheRowWorkshopTitle,
            Keys.SettingsCacheRowWorkshopDesc,
            "workshop_items / workshop_pages",
            () =>
            {
                // v2 起条目与分页都在自有库真表里，清完再把封面目录一起删掉。
                AppServices.WorkshopInfo.Clear();
                AppServices.WorkshopItemPages.Clear();
                CacheStorage.Clear(AppServices.FileSystem, AppServices.WorkshopCacheRoot);
            },
            documentKey: null,
            folderPath: AppServices.WorkshopCacheRoot,
            measure: () =>
            {
                var covers = CacheStorage.Measure(AppServices.FileSystem, AppServices.WorkshopCacheRoot);
                var entries = AppServices.WorkshopInfo.Count + AppServices.WorkshopItemPages.Count;
                return new CacheUsage(covers.Bytes, covers.Files + entries);
            }),
        new(
            CacheRowViewModel.TranslationsKey,
            Keys.SettingsCacheRowTranslationsTitle,
            Keys.SettingsCacheRowTranslationsDesc,
            JsonDocumentKeys.Translations,
            () => AppServices.Translation.ClearCache(),
            JsonDocumentKeys.Translations,
            null),
    };

    /// <summary>
    /// 重新统计一批缓存行的占用（只看文件属性，不联网）。只做 IO，不碰界面状态：
    /// 行的 <see cref="CacheRowViewModel.UpdateUsage"/> 由调用方在 UI 线程回填。
    /// </summary>
    public static async Task<CacheUsage[]> MeasureAsync(
        IReadOnlyList<CacheRowViewModel> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return Array.Empty<CacheUsage>();
        }

        var fileSystem = AppServices.FileSystem;
        var documents = AppServices.Documents;
        return await Task.Run(
            () =>
            {
                var result = new CacheUsage[rows.Count];
                for (var index = 0; index < rows.Count; index++)
                {
                    result[index] = rows[index].MeasureOverride?.Invoke()
                        ?? CacheStorage.Measure(
                            fileSystem,
                            documents,
                            rows[index].DocumentKey,
                            rows[index].FolderPath);
                }

                return result;
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>逐行清除全部缓存（在后台线程执行，可能删不少文件）。</summary>
    public static async Task ClearAllAsync(
        IReadOnlyList<CacheRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        await Task.Run(
            () =>
            {
                foreach (var row in rows.ToArray())
                {
                    row.Clear();
                }
            },
            cancellationToken).ConfigureAwait(true);
    }
}
