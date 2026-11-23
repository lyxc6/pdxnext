using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Legal.Models;

namespace PDX.SDK.Contracts.Service.Legal.Result;

[Preserve]
public class GetAllDocumentResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<Document> Documents { get; set; }
}
