using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Loyalty.Models;

public class Rule
{
	public string Event { get; set; }

	public string Type { get; set; }

	public string Metadata { get; set; }

	public string Description { get; set; }

	public List<Reward> Rewards { get; set; }
}
