using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Internal.Cache.Models;

namespace PDX.SDK.Internal.Cache;

internal class SessionCommand : ISessionCommand
{
	private readonly IUpdateHandle<CachedData> _updateHandle;

	public SessionCommand(IUpdateHandle<CachedData> updateHandle)
	{
		_updateHandle = updateHandle;
	}

	public Task<UpdateResult<ISessionData>> SetSession(string sessionToken, string refreshToken, TrustLevel trustLevel, string @namespace)
	{
		return _updateHandle.Update<ISessionData>(delegate(CachedData state)
		{
			state.SessionToken = sessionToken;
			if (refreshToken != null)
			{
				state.RefreshToken = refreshToken;
			}
			state.TrustLevel = trustLevel;
			state.Namespace = @namespace;
		});
	}

	public Task<UpdateResult<ISessionData>> ClearSession()
	{
		return _updateHandle.Update<ISessionData>(delegate(CachedData state)
		{
			state.SessionToken = null;
			state.RefreshToken = null;
			state.TrustLevel = TrustLevel.Undefined;
			state.Namespace = null;
		});
	}
}
