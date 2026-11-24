using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Legal.Response;

[Preserve]
internal class SimplifiedDocumentResponse
{
	[Preserve]
	public string title { get; set; }

	[Preserve]
	public string type { get; set; }

	[Preserve]
	public bool required { get; set; }

	[Preserve]
	public bool showOncePerInstall { get; set; }
}
