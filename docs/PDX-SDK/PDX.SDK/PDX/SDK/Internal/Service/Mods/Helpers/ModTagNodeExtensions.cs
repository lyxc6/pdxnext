using System.Linq;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Internal.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods.Helpers;

internal static class ModTagNodeExtensions
{
	public static ModTagNode ToModTagNode(this InternalModNode internalModNode)
	{
		return new ModTagNode
		{
			TagName = internalModNode.tagName,
			ParentName = internalModNode.parentName,
			DisplayName = internalModNode.displayName,
			Selectable = internalModNode.selectable,
			Hidden = internalModNode.hidden,
			UsageCount = internalModNode.usageCount,
			Order = internalModNode.order,
			Children = internalModNode.children.Select((InternalModNode modNode) => modNode.ToModTagNode()).ToList()
		};
	}
}
