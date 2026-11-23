using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class ZipContentResult : Result
{
	[Preserve]
	public string ZipPath { get; set; }
}
