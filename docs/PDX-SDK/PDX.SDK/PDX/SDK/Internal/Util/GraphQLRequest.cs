using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Util;

[Preserve]
internal class GraphQLRequest
{
	[Preserve]
	public string query { get; set; }

	[Preserve]
	public string operationName { get; set; }

	[Preserve]
	public Dictionary<string, string> variables { get; set; }
}
