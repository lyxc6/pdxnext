using System;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Contracts.Events.Mods;

namespace PDX.SDK.Internal.Events.Internal.Mods;

public class ModUnsubscribed : IModUnsubscribed, IEventBase
{
	public Guid EventId { get; set; }

	public int ManagedModId { get; set; } = -1;

	public string UnmanagedOrWipModName { get; set; }
}
