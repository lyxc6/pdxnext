using System;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class PlaysetInMod
{
	public int PlaysetId { get; set; }

	public string Name { get; set; }

	public DateTime? SubscribedDate { get; set; }

	public bool ModIsEnabled { get; set; }

	public string Version { get; set; }

	public int LoadOrder { get; set; }
}
