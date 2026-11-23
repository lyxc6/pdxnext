using System;

namespace PDX.SDK.Internal.Service.Mods.Models;

public class PlaysetGeneralMetaData
{
	public int Id { get; set; }

	public string Name { get; set; }

	public string Game { get; set; }

	public string DisplayImagePath { get; set; }

	public int ModsCount { get; set; }

	public ulong ModsSize { get; set; }

	public DateTime Updated { get; set; }

	public DateTime? PlaysetModsLatestUpdated { get; set; }

	public DateTime? UpdatedSyncedWithBackend { get; set; }
}
