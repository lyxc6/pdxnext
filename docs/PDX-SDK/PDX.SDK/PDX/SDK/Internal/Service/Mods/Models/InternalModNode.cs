using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Models;

[Preserve]
internal class InternalModNode
{
	[Preserve]
	public string tagName { get; set; }

	[Preserve]
	public string parentName { get; set; }

	[Preserve]
	public string displayName { get; set; }

	[Preserve]
	public bool selectable { get; set; }

	[Preserve]
	public bool hidden { get; set; }

	[Preserve]
	public int usageCount { get; set; }

	[Preserve]
	public int? order { get; set; }

	[Preserve]
	public List<InternalModNode> children { get; set; }
}
