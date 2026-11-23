using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Legal.Response;

[Preserve]
internal class GetAllDocumentsResponse : HTTPBodyBase
{
	[Preserve]
	public DocumentResponse[] documents { get; set; }
}
