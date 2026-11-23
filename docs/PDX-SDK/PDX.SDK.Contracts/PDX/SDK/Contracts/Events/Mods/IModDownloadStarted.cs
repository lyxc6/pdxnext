namespace PDX.SDK.Contracts.Events.Mods;

public interface IModDownloadStarted : IEventBase
{
	int ModId { get; }
}
