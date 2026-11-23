using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Util;

[Preserve]
internal class GraphQLResponse<T> : HTTPBodyBase where T : class
{
	[Preserve]
	public T data { get; set; }

	[Preserve]
	public List<GraphQLResponseError> errors { get; set; }
}
