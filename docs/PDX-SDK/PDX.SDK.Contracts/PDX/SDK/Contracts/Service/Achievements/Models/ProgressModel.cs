using System;

namespace PDX.SDK.Contracts.Service.Achievements.Models;

public class ProgressModel
{
	public bool IsAchieved { get; set; }

	public DateTime? AchievedDate { get; set; }

	public DateTime? UpdatedDate { get; set; }

	public int? Value { get; set; }
}
