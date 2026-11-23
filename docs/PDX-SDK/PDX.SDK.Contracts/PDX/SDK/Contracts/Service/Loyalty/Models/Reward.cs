using PDX.SDK.Contracts.Service.Loyalty.Enums;

namespace PDX.SDK.Contracts.Service.Loyalty.Models;

public class Reward
{
	public RewardType Type { get; set; }

	public int Received { get; set; }
}
