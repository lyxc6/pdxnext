using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.DataStorage.CloudSave.Models;

namespace PDX.SDK.Contracts.Service.DataStorage.CloudSave.Result;

[Preserve]
public class ListEntriesResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<Entry> Entries { get; set; }
}
