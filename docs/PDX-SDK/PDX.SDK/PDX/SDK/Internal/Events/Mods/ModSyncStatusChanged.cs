using System;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Contracts.Events.Mods;

namespace PDX.SDK.Internal.Events.Mods;

public class ModSyncStatusChanged : IModSyncStatusChanged, IEventBase
{
	public Guid EventId { get; } = Guid.NewGuid();

	public bool IsSyncOngoing { get; set; }
}
