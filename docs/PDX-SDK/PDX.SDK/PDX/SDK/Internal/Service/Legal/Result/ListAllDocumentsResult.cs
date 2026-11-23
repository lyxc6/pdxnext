using System.Collections.Generic;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Service.Legal.Models;

namespace PDX.SDK.Internal.Service.Legal.Result;

[Preserve]
internal class ListAllDocumentsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<DocumentFrontpage> Documents { get; set; }
}
