using System;

namespace PDX.SDK.Contracts.Service.Mods.Result;

public class ModVersionResult
{
	public string Id { get; set; }

	public DateTime? CreatedDate { get; set; }

	public ulong Size { get; set; }
}
