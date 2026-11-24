using System;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Contracts.Events.Mods;

namespace PDX.SDK.Internal.Events.Internal.Mods;

public class ModDownloadFailed : IModDownloadFailed, IEventBase
{
	public Guid EventId { get; set; }

	public int ModId { get; set; }
}
