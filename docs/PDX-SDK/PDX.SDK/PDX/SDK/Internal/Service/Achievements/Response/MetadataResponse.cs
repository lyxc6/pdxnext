using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Achievements.Response;

[Preserve]
internal class MetadataResponse
{
	[Preserve]
	public Dictionary<string, string> descriptions { get; set; }

	[Preserve]
	public bool hidden { get; set; }

	[Preserve]
	public string name { get; set; }

	[Preserve]
	public int progress_max { get; set; }

	[Preserve]
	public int progress_min { get; set; }

	[Preserve]
	public Dictionary<string, string> titles { get; set; }
}
