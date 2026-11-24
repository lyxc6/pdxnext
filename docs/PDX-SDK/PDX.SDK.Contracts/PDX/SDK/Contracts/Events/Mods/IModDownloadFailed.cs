namespace PDX.SDK.Contracts.Events.Mods;

public interface IModDownloadFailed : IEventBase
{
	int ModId { get; }
}
