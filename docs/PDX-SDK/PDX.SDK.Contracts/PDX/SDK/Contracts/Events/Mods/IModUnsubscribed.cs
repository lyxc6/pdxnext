namespace PDX.SDK.Contracts.Events.Mods;

public interface IModUnsubscribed : IEventBase
{
	int ManagedModId { get; }
}
