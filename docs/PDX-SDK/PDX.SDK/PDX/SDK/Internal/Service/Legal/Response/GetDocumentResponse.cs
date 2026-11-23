using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Legal.Response;

[Preserve]
internal class GetDocumentResponse : HTTPBodyBase
{
	[Preserve]
	public DocumentResponse document { get; set; }
}
