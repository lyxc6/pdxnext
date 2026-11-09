// 仅供 PdxLauncher.Checks 链接 App/ViewModels/AchievementsViewModel.cs 使用的最小本地化替身。
// 真实实现依赖 WinUI 工程的 Localizer 与 resw；自检只关心状态机与文案键，不需要真译文。
namespace PdxLauncher.App.Localization;

using System.Collections.Generic;
using System.Linq;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>与 App 同名的 ViewModel 基类替身：提供 OnPropertyChanged / SetProperty / T。</summary>
public abstract class LocalizedObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected static string T(string key, params object?[] args) =>
        args.Length == 0 ? key : key + ":" + string.Join("|", args.Select(static arg => arg?.ToString()));

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>与真实基类同名：语言切换后重建带本地化文字的集合（这里什么都不用做）。</summary>
    protected virtual void Relocalize()
    {
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>与 App 同名的 Localizer 替身：只把「键 + 参数」拼成可比较的字符串。</summary>
internal sealed class Localizer
{
    public static Localizer Instance { get; } = new();

    public string T(string key, params object?[] args) =>
        args.Length == 0 ? key : key + ":" + string.Join("|", args.Select(static arg => arg?.ToString()));
}

/// <summary>成就相关文案键；取值与 Resources.resw 一致，避免自检与真实键名漂移。</summary>
internal static class Keys
{
    public const string AchievementsCounts = "achievements.counts";
    public const string AchievementsEmptyTitle = "achievements.empty.title";
    public const string AchievementsFilterAll = "achievements.filter.all";
    public const string AchievementsFilterCount = "achievements.filter.count";
    public const string AchievementsFilterEmptyMessage = "achievements.filter.empty.message";
    public const string AchievementsFilterEmptyTitle = "achievements.filter.empty.title";
    public const string AchievementsFilterLocked = "achievements.filter.locked";
    public const string AchievementsFilterName = "achievements.filter.name";
    public const string AchievementsFilterUnlocked = "achievements.filter.unlocked";
    public const string AchievementsHiddenDescription = "achievements.hiddenDescription";
    public const string AchievementsLastUpdated = "achievements.lastUpdated";
    public const string AchievementsRarity = "achievements.rarity";
    public const string AchievementsSearchName = "achievements.search.name";
    public const string AchievementsSearchPlaceholder = "achievements.search.placeholder";
    public const string AchievementsStatusHidden = "achievements.statusHidden";
    public const string AchievementsStatusLocked = "achievements.statusLocked";
    public const string AchievementsStatusUnlocked = "achievements.statusUnlocked";
    public const string AchievementsStatusUnlockedAt = "achievements.statusUnlockedAt";
    public const string AchievementsUnavailableEmpty = "achievements.unavailable.empty";
    public const string AchievementsUnavailableNoGame = "achievements.unavailable.noGame";
    public const string AchievementsUnavailableNotSupported = "achievements.unavailable.notSupported";
    public const string AchievementsUnavailableSteamUnavailable = "achievements.unavailable.steamUnavailable";
    public const string SavesActionBackup = "saves.action.backup";
    public const string SavesActionDelete = "saves.action.delete";
    public const string SavesActionDeleteBackup = "saves.action.deleteBackup";
    public const string SavesActionManageBackups = "saves.action.manageBackups";
    public const string SavesActionPruneBackups = "saves.action.pruneBackups";
    public const string SavesActionRestore = "saves.action.restore";
    public const string SavesActionRestoreOverwrite = "saves.action.restoreOverwrite";
    public const string SavesBackupAutoPruned = "saves.backup.autoPruned";
    public const string SavesBackupCreated = "saves.backup.created";
    public const string SavesBackupEmpty = "saves.backup.empty";
    public const string SavesBackupOpenFolder = "saves.backup.openFolder";
    public const string SavesBackupOpenFolderFailed = "saves.backup.openFolderFailed";
    public const string SavesBackupOrphan = "saves.backup.orphan";
    public const string SavesBackupPageBack = "saves.backup.pageBack";
    public const string SavesBackupQuotaFull = "saves.backup.quotaFull";
    public const string SavesBackupQuotaHint = "saves.backup.quotaHint";
    public const string SavesBackupQuotaLabel = "saves.backup.quotaLabel";
    public const string SavesBackupRowName = "saves.backup.rowName";
    public const string SavesBackupTitle = "saves.backup.title";
    public const string SavesBackupUsage = "saves.backup.usage";
    public const string SavesCity = "saves.city";
    public const string SavesConfirmDeleteBackupContent = "saves.confirm.deleteBackup.content";
    public const string SavesConfirmDeleteBackupTitle = "saves.confirm.deleteBackup.title";
    public const string SavesConfirmDeleteSaveContent = "saves.confirm.deleteSave.content";
    public const string SavesConfirmDeleteSaveTitle = "saves.confirm.deleteSave.title";
    public const string SavesConfirmPruneBackupsContent = "saves.confirm.pruneBackups.content";
    public const string SavesConfirmPruneBackupsTitle = "saves.confirm.pruneBackups.title";
    public const string SavesConfirmRestoreContent = "saves.confirm.restore.content";
    public const string SavesConfirmRestoreOverwriteContent = "saves.confirm.restoreOverwrite.content";
    public const string SavesConfirmRestoreOverwriteTitle = "saves.confirm.restoreOverwrite.title";
    public const string SavesConfirmRestoreTitle = "saves.confirm.restore.title";
    public const string SavesCountry = "saves.country";
    public const string SavesDateFallback = "saves.dateFallback";
    public const string SavesEmptyEmptyMessage = "saves.empty.empty.message";
    public const string SavesEmptyEmptyTitle = "saves.empty.empty.title";
    public const string SavesEmptyFailedMessage = "saves.empty.failed.message";
    public const string SavesEmptyFailedTitle = "saves.empty.failed.title";
    public const string SavesEmptyNoDirectoryMessage = "saves.empty.noDirectory.message";
    public const string SavesEmptyNoDirectoryTitle = "saves.empty.noDirectory.title";
    public const string SavesEmptyNoGameMessage = "saves.empty.noGame.message";
    public const string SavesEmptyNoGameTitle = "saves.empty.noGame.title";
    public const string SavesEmptyUnsupportedMessage = "saves.empty.unsupported.message";
    public const string SavesEmptyUnsupportedTitle = "saves.empty.unsupported.title";
    public const string SavesExperimental = "saves.experimental";
    public const string SavesExperimentalTooltip = "saves.experimentalTooltip";
    public const string SavesMetadataUnreadable = "saves.metadataUnreadable";
    public const string SavesNoGame = "saves.noGame";
    public const string SavesNoticeBackedUp = "saves.notice.backedUp";
    public const string SavesNoticeDeletedBackup = "saves.notice.deletedBackup";
    public const string SavesNoticeDeletedSave = "saves.notice.deletedSave";
    public const string SavesNoticePruned = "saves.notice.pruned";
    public const string SavesNoticeRestored = "saves.notice.restored";
    public const string SavesNoticeSourceMissing = "saves.notice.sourceMissing";
    public const string SavesRowNamePair = "saves.rowNamePair";
    public const string SavesSaveName = "saves.saveName";
    public const string SavesStatusFailed = "saves.status.failed";
    public const string SavesStatusLastScan = "saves.status.lastScan";
    public const string SavesStatusNoGame = "saves.status.noGame";
    public const string SavesStatusReading = "saves.status.reading";
    public const string SavesStatusScanning = "saves.status.scanning";
    public const string SavesStatusUnsupported = "saves.status.unsupported";
    public const string WorkshopDependenciesGroupAttention = "workshop.dependencies.groupAttention";
    public const string WorkshopDependenciesGroupReady = "workshop.dependencies.groupReady";
    public const string WorkshopDependenciesGroupUnknown = "workshop.dependencies.groupUnknown";
    public const string WorkshopDependencyDisabled = "workshop.dependency.disabled";
    public const string WorkshopDependencyEnabled = "workshop.dependency.enabled";
    public const string WorkshopDependencyMissing = "workshop.dependency.missing";
    public const string WorkshopDependencyUnknown = "workshop.dependency.unknown";
    public const string WorkshopDependencyVersion = "workshop.dependency.version";
    public const string SavesVersion = "saves.version";

    // DLC 行视图模型（DlcImageChecks 链接 DlcRowViewModel.cs 用）。取值与 Resources.resw 一致。
    public const string DlcContentFilesText = "dlc.contentFilesText";
    public const string DlcContentFilesToolTip = "dlc.contentFilesToolTip";
    public const string DlcContentMissingText = "dlc.contentMissingText";
    public const string DlcContentMissingToolTip = "dlc.contentMissingToolTip";
    public const string DlcContentSteamText = "dlc.contentSteamText";
    public const string DlcContentSteamToolTip = "dlc.contentSteamToolTip";
    public const string DlcOwnedBadge = "dlc.ownedBadge";
    public const string DlcOwnershipRowNoSteam = "dlc.ownershipRowNoSteam";
    public const string DlcOwnershipRowToolTip = "dlc.ownershipRowToolTip";
    public const string DlcPatchAppliedBadge = "dlc.patchAppliedBadge";
    public const string DlcPatchCoverageToolTip = "dlc.patchCoverageToolTip";
    public const string DlcPatchCoveredBadge = "dlc.patchCoveredBadge";
    public const string DlcPatchOverwrittenBadge = "dlc.patchOverwrittenBadge";
    public const string DlcToggleToolTip = "dlc.toggleToolTip";
    public const string DlcToggleToolTipLocked = "dlc.toggleToolTipLocked";
    public const string DlcUnknownBadge = "dlc.unknownBadge";
    public const string DlcUnownedBadge = "dlc.unownedBadge";
}
