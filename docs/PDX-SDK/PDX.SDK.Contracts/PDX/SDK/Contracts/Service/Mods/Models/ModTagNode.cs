using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModTagNode
{
	public string TagName { get; set; }

	public string ParentName { get; set; }

	public string DisplayName { get; set; }

	public bool Selectable { get; set; }

	public bool Hidden { get; set; }

	public int UsageCount { get; set; }

	public int? Order { get; set; }

	public List<ModTagNode> Children { get; set; }
}
