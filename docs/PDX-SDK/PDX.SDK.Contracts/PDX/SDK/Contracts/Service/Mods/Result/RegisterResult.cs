using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class RegisterResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string Guid { get; set; }

	[Preserve]
	public string Path { get; set; }
}
