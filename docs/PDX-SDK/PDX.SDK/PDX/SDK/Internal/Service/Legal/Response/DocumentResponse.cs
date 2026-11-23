using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Legal.Response;

[Preserve]
internal class DocumentResponse
{
	[Preserve]
	public string title { get; set; }

	[Preserve]
	public string type { get; set; }

	[Preserve]
	public string text { get; set; }

	[Preserve]
	public string language { get; set; }

	[Preserve]
	public string url { get; set; }
}
