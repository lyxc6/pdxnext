namespace PDX.SDK.Contracts.Events.Mods;

public interface IModDownloadCompleted : IEventBase
{
	int ModId { get; }
}
