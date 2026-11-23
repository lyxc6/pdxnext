using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Util;

[Preserve]
internal class GraphQLResponseError
{
	[Preserve]
	public string message;

	[Preserve]
	public Dictionary<string, string> extensions;
}
