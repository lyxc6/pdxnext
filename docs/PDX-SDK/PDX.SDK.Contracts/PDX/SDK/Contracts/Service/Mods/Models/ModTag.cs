using PDX.SDK.Contracts.Service.Generic;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModTag : ITag
{
	public string Id { get; set; }

	public string DisplayName { get; set; }
}
