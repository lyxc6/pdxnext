using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Loyalty.Models;

public class ThresholdReached
{
	public string PointType { get; set; }

	public List<Threshold> Thresholds { get; set; }
}
