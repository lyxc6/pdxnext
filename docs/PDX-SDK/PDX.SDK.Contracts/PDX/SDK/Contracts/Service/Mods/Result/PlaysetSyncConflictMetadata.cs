using System;

namespace PDX.SDK.Contracts.Service.Mods.Result;

public class PlaysetSyncConflictMetadata
{
	public int Id { get; set; }

	public string Name { get; set; }

	public DateTime UpdatedDate { get; set; }
}
