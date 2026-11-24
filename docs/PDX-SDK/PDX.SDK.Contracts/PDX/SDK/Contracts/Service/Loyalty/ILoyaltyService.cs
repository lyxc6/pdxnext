using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.Loyalty.Result;

namespace PDX.SDK.Contracts.Service.Loyalty;

public interface ILoyaltyService
{
	Task<UserPointsResult> GetPoints(string pointType = null);

	Task<LoyaltyStatusResult> GetStatus(string @event = null, string type = null);

	Task<PDX.SDK.Contracts.Result> RegisterEvent(string @event, string type);
}
