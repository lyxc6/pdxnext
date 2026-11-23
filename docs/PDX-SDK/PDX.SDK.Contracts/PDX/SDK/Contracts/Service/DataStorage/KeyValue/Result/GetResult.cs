using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue.Models;

namespace PDX.SDK.Contracts.Service.DataStorage.KeyValue.Result;

[Preserve]
public class GetResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public Entry Entry { get; set; }
}
