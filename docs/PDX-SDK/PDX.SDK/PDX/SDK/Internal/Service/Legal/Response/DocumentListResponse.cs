using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Legal.Response;

[Preserve]
internal class DocumentListResponse : HTTPBodyBase
{
	[Preserve]
	public SimplifiedDocumentResponse[] documents { get; set; }
}
