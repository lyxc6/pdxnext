using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Events.Mods;

public interface IModLoadStatusChanged : IEventBase
{
	bool IsActivePlaysetUpdated { get; }

	IMod NewModToLoad { get; }

	IMod ModToUnload { get; }
}
