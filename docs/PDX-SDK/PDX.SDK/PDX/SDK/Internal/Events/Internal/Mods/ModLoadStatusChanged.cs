using System;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Contracts.Events.Mods;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Internal.Events.Internal.Mods;

public class ModLoadStatusChanged : IModLoadStatusChanged, IEventBase
{
	public Guid EventId { get; set; }

	public bool IsActivePlaysetUpdated { get; set; }

	public IMod NewModToLoad { get; set; }

	public IMod ModToUnload { get; set; }
}
