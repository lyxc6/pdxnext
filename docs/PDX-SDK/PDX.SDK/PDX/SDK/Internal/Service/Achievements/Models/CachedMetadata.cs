using System.Collections.Generic;

namespace PDX.SDK.Internal.Service.Achievements.Models;

public class CachedMetadata
{
	public Dictionary<string, string> Descriptions { get; set; }

	public bool Hidden { get; set; }

	public string Name { get; set; }

	public int ProgressMax { get; set; }

	public int ProgressMin { get; set; }

	public Dictionary<string, string> Titles { get; set; }
}
