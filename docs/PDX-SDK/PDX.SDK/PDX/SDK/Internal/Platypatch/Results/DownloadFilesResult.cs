using System.Collections.Generic;
using PDX.SDK.Contracts;

namespace PDX.SDK.Internal.Platypatch.Results;

public class DownloadFilesResult : Result
{
	public List<string> LocalPaths { get; set; }
}
