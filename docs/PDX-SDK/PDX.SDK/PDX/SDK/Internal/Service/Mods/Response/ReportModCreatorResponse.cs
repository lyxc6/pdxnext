using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

internal class ReportModCreatorResponse : HTTPBodyBase
{
	[Preserve]
	public int id { get; set; }
}
