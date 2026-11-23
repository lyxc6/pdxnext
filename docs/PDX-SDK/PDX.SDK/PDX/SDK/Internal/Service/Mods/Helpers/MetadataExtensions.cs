using System.Collections.Generic;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;

namespace PDX.SDK.Internal.Service.Mods.Helpers;

internal static class MetadataExtensions
{
	public static ulong ParseSizeInMemory(this Dictionary<string, string> metadata, int modId, ILogger logger = null, FlowData flowData = null)
	{
		if (!metadata.ContainsKey("size_in_memory"))
		{
			return 0uL;
		}
		if (ulong.TryParse(metadata["size_in_memory"]?.Replace(",", ""), out var result))
		{
			return result;
		}
		logger?.Log("Backend returned invalid size for mod id " + modId, LogLevel.L1_Debug, flowData);
		return 0uL;
	}
}
