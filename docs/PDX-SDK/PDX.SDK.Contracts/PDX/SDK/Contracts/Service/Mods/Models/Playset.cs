using System;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class Playset
{
	public int PlaysetId { get; set; }

	public string Name { get; set; }

	public string DisplayImagePath { get; set; }

	public int ModsCount { get; set; }

	public ulong ModsSize { get; set; }

	public DateTime? Updated { get; set; }

	public DateTime? LatestModUpdate { get; set; }
}
