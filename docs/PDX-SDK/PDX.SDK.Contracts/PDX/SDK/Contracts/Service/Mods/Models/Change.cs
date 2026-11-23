using System;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class Change
{
	public string Version { get; set; }

	public DateTime? ReleasedDate { get; set; }

	public string Details { get; set; }

	public ulong Size { get; set; }

	public string UserModVersion { get; set; }
}
