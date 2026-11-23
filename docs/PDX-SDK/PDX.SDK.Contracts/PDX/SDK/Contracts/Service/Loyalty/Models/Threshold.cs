using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Loyalty.Models;

public class Threshold
{
	public int Limit { get; set; }

	public List<Reward> Rewards { get; set; }
}
