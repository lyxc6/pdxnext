using System;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Internal.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods.Helpers;

public static class PlaysetAndModExtensions
{
	public static string GetKey(this IMod mod)
	{
		try
		{
			if (mod.Id > 0 && (mod.LocalData == null || mod.LocalData.LocalType == LocalType.Subscribed))
			{
				return mod.Id.GetKey(mod.Version);
			}
			return mod.Name;
		}
		catch (Exception)
		{
			return mod.Name;
		}
	}

	public static string GetKey(this ModDetails mod)
	{
		return mod.Id.GetKey(mod.Version);
	}

	public static string GetModKey(this int id, string version)
	{
		return id.GetKey(version);
	}

	public static string GetKey(this PlaysetSubscribedModMetaData playsetSubscribedMod)
	{
		return playsetSubscribedMod.Id.GetKey(playsetSubscribedMod.Version);
	}

	public static string GetKey(this PlaysetWipModMetaData playsetWipMod)
	{
		return playsetWipMod.Id;
	}

	public static string GetKey(this PlaysetUnmanagedModMetaData playsetUnmanagedMod)
	{
		return playsetUnmanagedMod.Path;
	}

	internal static string GetKey(this int id, string version)
	{
		return string.Format("{0}_{1}", id, string.IsNullOrEmpty(version) ? "-" : version);
	}

	internal static string GetKey(this PlaysetSubscribedMod playsetSubscribedMod)
	{
		return playsetSubscribedMod.Id.GetKey(playsetSubscribedMod.Version.ToString());
	}

	internal static string GetKey(this CachedPlaysetMetaData cachedPlaysetMetaData)
	{
		return cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.GetKey();
	}

	internal static string GetKey(this PlaysetGeneralMetaData playsetGeneralMetaData)
	{
		return ((!playsetGeneralMetaData.UpdatedSyncedWithBackend.HasValue) ? playsetGeneralMetaData.Id.GetLocalPlaysetKey() : playsetGeneralMetaData.Id.ToString()) ?? "";
	}

	internal static void UpdateMetaFromRemovedMod(this PlaysetGeneralMetaData playsetGeneralMetaData, ulong modSize)
	{
		playsetGeneralMetaData.ModsCount--;
		playsetGeneralMetaData.ModsSize -= modSize;
		playsetGeneralMetaData.Updated = DateTime.Now;
	}

	internal static void UpdateMetaFromAddedMod(this PlaysetGeneralMetaData playsetGeneralMetaData, ulong modSize)
	{
		playsetGeneralMetaData.ModsCount++;
		playsetGeneralMetaData.ModsSize += modSize;
		playsetGeneralMetaData.Updated = DateTime.Now;
	}

	internal static bool IsSubscribedMod(this IMod mod)
	{
		if (mod.LocalData != null)
		{
			return mod.LocalData.LocalType == LocalType.Subscribed;
		}
		return false;
	}

	internal static bool IsSubscribedMod(this IPlaysetMod mod)
	{
		if (mod.LocalData != null)
		{
			return mod.LocalData.LocalType == LocalType.Subscribed;
		}
		return false;
	}
}
