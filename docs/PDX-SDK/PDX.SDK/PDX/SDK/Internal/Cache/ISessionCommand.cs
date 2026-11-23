using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Internal.Cache.Models;

namespace PDX.SDK.Internal.Cache;

internal interface ISessionCommand
{
	Task<UpdateResult<ISessionData>> SetSession(string sessionToken, string refreshToken, TrustLevel trustLevel, string @namespace);

	Task<UpdateResult<ISessionData>> ClearSession();
}
