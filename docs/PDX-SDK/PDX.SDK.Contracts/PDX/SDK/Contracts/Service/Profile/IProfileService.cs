using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.Profile.Result;

namespace PDX.SDK.Contracts.Service.Profile;

public interface IProfileService
{
	Task<PDX.SDK.Contracts.Result> CreateGameDisplayName(string displayName);

	Task<PDX.SDK.Contracts.Result> CreateSocialDisplayName(string displayName);

	Task<GetProfileResult> Get();
}
