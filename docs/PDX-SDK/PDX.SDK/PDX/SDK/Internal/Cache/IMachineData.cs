using System.Collections.Generic;

namespace PDX.SDK.Internal.Cache;

internal interface IMachineData
{
	List<string> ViewedDocuments { get; }
}
