namespace PDX.SDK.Contracts.Events.Mods;

public interface IModSyncStatusChanged : IEventBase
{
	bool IsSyncOngoing { get; }
}
