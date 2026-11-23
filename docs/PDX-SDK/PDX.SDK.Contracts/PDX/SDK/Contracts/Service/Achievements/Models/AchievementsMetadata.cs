using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Achievements.Models;

public class AchievementsMetadata
{
	public string Name { get; set; }

	public Dictionary<string, string> Titles { get; set; }

	public Dictionary<string, string> Descriptions { get; set; }

	public int? ProgressMax { get; set; }

	public int? ProgressMin { get; set; }

	public bool Hidden { get; set; }

	public byte[] AchievedImage { get; set; }

	public byte[] UnachievedImage { get; set; }
}
