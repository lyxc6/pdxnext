using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class PlaysetSyncResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public PlaysetSyncConflict[] PlaysetSyncConflicts { get; set; }
}
