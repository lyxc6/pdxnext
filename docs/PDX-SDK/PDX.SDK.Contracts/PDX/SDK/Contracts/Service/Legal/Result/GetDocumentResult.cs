using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Legal.Models;

namespace PDX.SDK.Contracts.Service.Legal.Result;

[Preserve]
public class GetDocumentResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public Document Document { get; set; }
}
