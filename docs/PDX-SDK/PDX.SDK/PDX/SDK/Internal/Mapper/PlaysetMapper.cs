using System.Collections.Generic;
using System.Linq;
using PDX.SDK.Contracts.Service.Generic;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.ThirdParty.Models;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Service.Mods.Response;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Mapper;

internal static class PlaysetMapper
{
	internal static Playset ToPlayset(this CachedPlaysetMetaData cachedPlaysetMetaData)
	{
		return new Playset
		{
			PlaysetId = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Id,
			Name = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Name,
			Updated = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Updated,
			LatestModUpdate = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.PlaysetModsLatestUpdated,
			ModsCount = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.ModsCount,
			ModsSize = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.ModsSize,
			DisplayImagePath = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.DisplayImagePath
		};
	}

	internal static PlaysetGeneralMetaData ToPlaysetGeneralMetadata(this Playset playset)
	{
		return new PlaysetGeneralMetaData
		{
			Id = playset.PlaysetId,
			Name = playset.Name,
			ModsCount = playset.ModsCount,
			ModsSize = playset.ModsSize,
			DisplayImagePath = playset.DisplayImagePath
		};
	}

	internal static Playset ToPlayset(this ListPlaysetResponse playsetResponse)
	{
		return new Playset
		{
			PlaysetId = playsetResponse.id,
			Name = playsetResponse.name,
			DisplayImagePath = playsetResponse.displayImagePath,
			ModsCount = playsetResponse.modsCount,
			ModsSize = playsetResponse.modsSize,
			Updated = playsetResponse.updated.ParseDateTime("Mods.ListAllPlaysets", "set.updated", "ModMapper"),
			LatestModUpdate = playsetResponse.latestModUpdate.ParseDateTime("Mods.ListAllPlaysets", "set.updated", "ModMapper")
		};
	}

	internal static PlaysetSubscribedMod ToPlaysetMod(this ModInPlaysetResponse modInPlaysetResponse)
	{
		PlaysetSubscribedMod playsetSubscribedMod = new PlaysetSubscribedMod
		{
			Id = modInPlaysetResponse.modId,
			ShortDescription = modInPlaysetResponse.shortDescription,
			RequiredGameVersion = modInPlaysetResponse.requiredVersion,
			DisplayName = modInPlaysetResponse.displayName,
			Version = (modInPlaysetResponse.preferredVersion.HasValue ? modInPlaysetResponse.preferredVersion.ToString() : modInPlaysetResponse.latestVersion),
			IsEnabled = modInPlaysetResponse.enabled,
			LoadOrder = modInPlaysetResponse.loadOrder,
			DisplayImagePath = modInPlaysetResponse.displayImagePath,
			Size = modInPlaysetResponse.size,
			Tags = ((IEnumerable<ITag>)modInPlaysetResponse.tags?.Select((string t) => new ModTag
			{
				Id = t,
				DisplayName = t
			})).ToArray(),
			Updated = modInPlaysetResponse.updated.ParseDateTime("Mods.GetModsInPlayset", "mod.updated", "ModMapper"),
			State = modInPlaysetResponse.state.GetModState(),
			UserModVersion = modInPlaysetResponse.userModVersion,
			RatingsTotal = modInPlaysetResponse.ratingsTotal,
			Author = modInPlaysetResponse.author,
			HasLiked = modInPlaysetResponse.hasLiked,
			CreationDate = modInPlaysetResponse.creationDate.ParseDateTime("Mods.GetModsInPlayset", "mod.creationDate", "ModMapper")
		};
		if (modInPlaysetResponse.thirdPartyLinks != null && modInPlaysetResponse.thirdPartyLinks.Length != 0)
		{
			playsetSubscribedMod.ThirdPartyProfiles = new List<ThirdPartyProfile>();
			ThirdPartyLinksResponse[] thirdPartyLinks = modInPlaysetResponse.thirdPartyLinks;
			foreach (ThirdPartyLinksResponse thirdPartyLinksResponse in thirdPartyLinks)
			{
				playsetSubscribedMod.ThirdPartyProfiles.Add(new ThirdPartyProfile
				{
					Provider = ProviderExtensions.GetProvider(thirdPartyLinksResponse.platform),
					Id = thirdPartyLinksResponse.thirdPartyId,
					DisplayName = thirdPartyLinksResponse.thirdPartyDisplayName
				});
			}
		}
		return playsetSubscribedMod;
	}

	internal static PlaysetSubscribedMod ToPlaysetMod(this PlaysetSubscribedModMetaData playsetSubscribedModMetaData, IMod mod)
	{
		PlaysetSubscribedMod obj = new PlaysetSubscribedMod
		{
			Id = playsetSubscribedModMetaData.Id,
			DisplayName = mod.DisplayName,
			Version = playsetSubscribedModMetaData.Version,
			IsEnabled = playsetSubscribedModMetaData.IsEnabled,
			LoadOrder = playsetSubscribedModMetaData.LoadOrder,
			DisplayImagePath = mod.ThumbnailPath,
			Size = mod.Size
		};
		ITag[] tags = mod.Tags?.ToArray();
		obj.Tags = tags;
		obj.Updated = mod.LatestUpdate;
		obj.State = mod.State;
		obj.LocalData = mod.LocalData;
		obj.ShortDescription = mod.ShortDescription;
		obj.RequiredGameVersion = mod.RequiredGameVersion;
		obj.UserModVersion = mod.UserModVersion;
		obj.RatingsTotal = mod.RatingsTotal;
		return obj;
	}

	internal static PlaysetSubscribedModMetaData ToPlaysetSubscribedModMetaData(this PlaysetSubscribedMod subscribedMod)
	{
		return new PlaysetSubscribedModMetaData
		{
			Id = subscribedMod.Id,
			Version = subscribedMod.Version,
			IsEnabled = subscribedMod.IsEnabled,
			LoadOrder = subscribedMod.LoadOrder
		};
	}

	internal static PlaysetWipMod ToPlaysetMod(this PlaysetWipModMetaData playsetWipModMetaData, IMod mod)
	{
		PlaysetWipMod obj = new PlaysetWipMod
		{
			Id = playsetWipModMetaData.Id,
			DisplayName = mod.DisplayName,
			IsEnabled = playsetWipModMetaData.IsEnabled,
			LoadOrder = playsetWipModMetaData.LoadOrder,
			DisplayImagePath = mod.ThumbnailPath,
			Size = mod.Size
		};
		ITag[] tags = mod.Tags?.ToArray();
		obj.Tags = tags;
		obj.Updated = mod.LatestUpdate;
		obj.LocalData = mod.LocalData;
		return obj;
	}

	internal static PlaysetUnmanagedMod ToPlaysetMod(this PlaysetUnmanagedModMetaData playsetUnmanagedModMetaData, IMod mod)
	{
		return new PlaysetUnmanagedMod
		{
			Id = playsetUnmanagedModMetaData.Path,
			DisplayName = playsetUnmanagedModMetaData.Path,
			IsEnabled = playsetUnmanagedModMetaData.IsEnabled,
			LoadOrder = playsetUnmanagedModMetaData.LoadOrder,
			LocalData = mod.LocalData
		};
	}

	internal static PlaysetInMod ToPlaysetInSubscribedMod(this CachedPlaysetMetaData cachedPlaysetMetaData, string modKey)
	{
		PlaysetSubscribedModMetaData playsetSubscribedModMetaData = cachedPlaysetMetaData.SubscribedModsTracked.First((PlaysetSubscribedModMetaData m) => m.GetKey() == modKey);
		return new PlaysetInMod
		{
			PlaysetId = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Id,
			Name = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Name,
			Version = playsetSubscribedModMetaData.Version,
			ModIsEnabled = playsetSubscribedModMetaData.IsEnabled,
			LoadOrder = playsetSubscribedModMetaData.LoadOrder
		};
	}

	internal static PlaysetInMod ToPlaysetInWipMod(this CachedPlaysetMetaData cachedPlaysetMetaData, string modKey)
	{
		PlaysetWipModMetaData playsetWipModMetaData = cachedPlaysetMetaData.WipModsTracked.First((PlaysetWipModMetaData m) => m.GetKey() == modKey);
		return new PlaysetInMod
		{
			PlaysetId = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Id,
			Name = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Name,
			ModIsEnabled = playsetWipModMetaData.IsEnabled,
			LoadOrder = playsetWipModMetaData.LoadOrder
		};
	}

	internal static PlaysetInMod ToPlaysetInUnmanagedMod(this CachedPlaysetMetaData cachedPlaysetMetaData, string modKey)
	{
		PlaysetUnmanagedModMetaData playsetUnmanagedModMetaData = cachedPlaysetMetaData.UnmanagedModsTracked.First((PlaysetUnmanagedModMetaData m) => m.GetKey() == modKey);
		return new PlaysetInMod
		{
			PlaysetId = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Id,
			Name = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Name,
			ModIsEnabled = playsetUnmanagedModMetaData.IsEnabled,
			LoadOrder = playsetUnmanagedModMetaData.LoadOrder
		};
	}
}
