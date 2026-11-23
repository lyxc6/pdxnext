namespace PDX.SDK.Contracts.Service.Mods.Result;

public class PlaysetSyncConflict
{
	public PlaysetSyncConflictMetadata OnlinePlayset { get; set; }

	public PlaysetSyncConflictMetadata LocalPlayset { get; set; }
}
