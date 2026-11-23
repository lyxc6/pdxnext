using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue.Models;

namespace PDX.SDK.Contracts.Service.DataStorage.KeyValue.Result;

[Preserve]
public class GetAllResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<Entry> Entries { get; set; }
}
