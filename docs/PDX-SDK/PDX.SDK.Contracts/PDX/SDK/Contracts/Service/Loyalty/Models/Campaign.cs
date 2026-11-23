using System;
using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Loyalty.Models;

public class Campaign
{
	public string Name { get; set; }

	public DateTime? Start { get; set; }

	public DateTime? End { get; set; }

	public string Description { get; set; }

	public List<Rule> Rules { get; set; }
}
