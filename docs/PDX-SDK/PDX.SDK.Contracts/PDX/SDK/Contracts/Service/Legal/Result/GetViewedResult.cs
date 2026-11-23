using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Legal.Result;

[Preserve]
public class GetViewedResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<string> DocumentTitles;
}
