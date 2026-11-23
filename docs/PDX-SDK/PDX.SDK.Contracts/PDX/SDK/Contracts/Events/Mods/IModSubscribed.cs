namespace PDX.SDK.Contracts.Events.Mods;

public interface IModSubscribed : IEventBase
{
	int ManagedModId { get; }
}
