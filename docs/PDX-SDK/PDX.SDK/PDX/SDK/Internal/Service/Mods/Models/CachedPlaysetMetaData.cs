using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Service.Mods.Models;

internal class CachedPlaysetMetaData
{
	public int ContractFormatVersion { get; } = 1;

	public ChangeTracked<PlaysetGeneralMetaData> GeneralDataTracked { get; }

	public ChangeTrackedConcurrentDictionary<PlaysetSubscribedModMetaData> SubscribedModsTracked { get; }

	public ChangeTrackedConcurrentDictionary<PlaysetWipModMetaData> WipModsTracked { get; }

	public ChangeTrackedConcurrentDictionary<PlaysetUnmanagedModMetaData> UnmanagedModsTracked { get; }

	public CachedPlaysetMetaData(PlaysetGeneralMetaData generalDataTracked, ChangeTrackedConcurrentDictionary<PlaysetSubscribedModMetaData> subscribedModsTracked, ChangeTrackedConcurrentDictionary<PlaysetWipModMetaData> wipModsTracked, ChangeTrackedConcurrentDictionary<PlaysetUnmanagedModMetaData> unmanagedModsTracked)
	{
		GeneralDataTracked = new ChangeTracked<PlaysetGeneralMetaData>(generalDataTracked);
		SubscribedModsTracked = subscribedModsTracked;
		WipModsTracked = wipModsTracked;
		UnmanagedModsTracked = unmanagedModsTracked;
	}

	private CachedPlaysetMetaData(PlaysetGeneralMetaData generalDataTracked, IPathFactory pathFactory, IFileIO fileIo, ILogger logger = null)
	{
		GeneralDataTracked = new ChangeTracked<PlaysetGeneralMetaData>(generalDataTracked);
		SubscribedModsTracked = new ChangeTrackedConcurrentDictionary<PlaysetSubscribedModMetaData>(new JournalManager(ModPath.GetPlaysetModsJournalName("playset_subscribed_mods", generalDataTracked.Id), pathFactory, fileIo, logger));
		WipModsTracked = new ChangeTrackedConcurrentDictionary<PlaysetWipModMetaData>(new JournalManager(ModPath.GetPlaysetModsJournalName("playset_wip_mods", generalDataTracked.Id), pathFactory, fileIo, logger));
		UnmanagedModsTracked = new ChangeTrackedConcurrentDictionary<PlaysetUnmanagedModMetaData>(new JournalManager(ModPath.GetPlaysetModsJournalName("playset_unmanaged_mods", generalDataTracked.Id), pathFactory, fileIo, logger));
	}

	private static async Task Initialize(CachedPlaysetMetaData cachedPlaysetMetaData)
	{
		await cachedPlaysetMetaData.SubscribedModsTracked.Initialize().ConfigureAwait(continueOnCapturedContext: false);
		await cachedPlaysetMetaData.WipModsTracked.Initialize().ConfigureAwait(continueOnCapturedContext: false);
		await cachedPlaysetMetaData.UnmanagedModsTracked.Initialize().ConfigureAwait(continueOnCapturedContext: false);
	}

	public static async Task<CachedPlaysetMetaData> Create(PlaysetGeneralMetaData generalDataTracked, IPathFactory pathFactory, IFileIO fileIo, ILogger logger = null)
	{
		CachedPlaysetMetaData ret = new CachedPlaysetMetaData(generalDataTracked, pathFactory, fileIo, logger);
		await Initialize(ret).ConfigureAwait(continueOnCapturedContext: false);
		return ret;
	}

	public static async Task<CachedPlaysetMetaData> Create(PlaysetGeneralMetaData generalDataTracked, Dictionary<string, PlaysetSubscribedModMetaData> subscribedModsTracked, Dictionary<string, PlaysetWipModMetaData> wipModsTracked, Dictionary<string, PlaysetUnmanagedModMetaData> unmanagedModsTracked, IPathFactory pathFactory, IFileIO fileIo, ILogger logger = null)
	{
		return new CachedPlaysetMetaData(generalDataTracked, await subscribedModsTracked.ToChangeTrackedConcurrentDictionary(new JournalManager(ModPath.GetPlaysetModsJournalName("playset_subscribed_mods", generalDataTracked.Id), pathFactory, fileIo, logger)).ConfigureAwait(continueOnCapturedContext: false), await wipModsTracked.ToChangeTrackedConcurrentDictionary(new JournalManager(ModPath.GetPlaysetModsJournalName("playset_wip_mods", generalDataTracked.Id), pathFactory, fileIo, logger)).ConfigureAwait(continueOnCapturedContext: false), await unmanagedModsTracked.ToChangeTrackedConcurrentDictionary(new JournalManager(ModPath.GetPlaysetModsJournalName("playset_unmanaged_mods", generalDataTracked.Id), pathFactory, fileIo, logger)).ConfigureAwait(continueOnCapturedContext: false));
	}

	public async Task UpdatePlaysetId(int id)
	{
		GeneralDataTracked.TrackedObject.Id = id;
		await SubscribedModsTracked.UpdateJournalName(ModPath.GetPlaysetModsJournalName("playset_subscribed_mods", id));
		await WipModsTracked.UpdateJournalName(ModPath.GetPlaysetModsJournalName("playset_subscribed_mods", id));
		await UnmanagedModsTracked.UpdateJournalName(ModPath.GetPlaysetModsJournalName("playset_subscribed_mods", id));
	}

	public async Task CleanUpJournal()
	{
		await SubscribedModsTracked.CleanUp().ConfigureAwait(continueOnCapturedContext: false);
		await WipModsTracked.CleanUp().ConfigureAwait(continueOnCapturedContext: false);
		await UnmanagedModsTracked.CleanUp().ConfigureAwait(continueOnCapturedContext: false);
	}
}
