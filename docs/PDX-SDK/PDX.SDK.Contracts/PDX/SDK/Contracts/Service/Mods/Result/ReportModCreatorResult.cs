using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class ReportModCreatorResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public int Id { get; set; }
}
