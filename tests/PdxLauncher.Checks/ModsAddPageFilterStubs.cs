// 仅供 PdxLauncher.Checks 链接 App/ViewModels/AddModsViewModel.cs 使用的最小替身。
// 真实 AppServices / TranslationCoordinator / DispatcherQueue 依赖 WinUI 与整份应用装配，
// 自检只关心筛选状态机、行复用与切语言还原，因此这里提供同名同签名的最小实现。
namespace PdxLauncher.App.Services
{
    using PdxLauncher.Core.Abstractions;
    using PdxLauncher.Core.Configuration;
    using PdxLauncher.Core.Online.Translation;

    /// <summary>名称翻译目标语言替身：默认关闭，不会真正起翻译批次。</summary>
    internal sealed class StubTranslationCoordinator
    {
        public TranslationOptions CurrentOptions { get; set; } = new(
            false,
            "auto",
            null,
            null,
            "default",
            null,
            null,
            null,
            TranslationOptions.English,
            30);

        public string? LastFailure => null;

        public void ResetRetryableFailures()
        {
        }

        public Task<IReadOnlyDictionary<Guid, TranslationCache.NameTranslation>> LoadNameTranslationsAsync(
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, TranslationCache.NameTranslation>>(
                new Dictionary<Guid, TranslationCache.NameTranslation>());

        public Task SaveNameTranslationsAsync(
            string targetLanguage,
            IReadOnlyList<TranslationCache.NameTranslation> translations,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string?>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken,
            Action<int, string?>? onItemTranslated = null,
            bool forceRefresh = false) => Task.FromResult<IReadOnlyList<string?>>(new string?[texts.Count]);
    }

    /// <summary>与 App 同名的服务定位器替身：只提供 AddModsViewModel 用到的 Settings / Translation。</summary>
    internal static class AppServices
    {
        public static AppSettings Settings { get; } = new AppSettings { TranslateNamesEnabled = false };

        public static StubTranslationCoordinator Translation { get; } = new();
    }
}

namespace Microsoft.UI.Dispatching
{
    // AddModsViewModel 用全限定名引用 DispatcherQueue；这里给一个「总在 UI 线程」的最小替身，
    // 让 ApplyTranslatedName 的切线程分支在自检里走同步路径（翻译默认关闭，实际上也不会走到）。
    internal sealed class DispatcherQueue
    {
        public static DispatcherQueue GetForCurrentThread() => new();

        public bool HasThreadAccess => true;

        public bool TryEnqueue(Action action)
        {
            action();
            return true;
        }
    }
}
