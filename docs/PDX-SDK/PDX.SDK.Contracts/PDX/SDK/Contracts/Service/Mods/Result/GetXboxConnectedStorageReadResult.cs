using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetXboxConnectedStorageReadResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string Value { get; set; }
}
