using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Legal.Models;
using PDX.SDK.Internal.Cache.Models;

namespace PDX.SDK.Internal.Cache;

internal class PlayerCommand : IPlayerCommand
{
	private readonly IUpdateHandle<CachedData> _updateHandle;

	public PlayerCommand(IUpdateHandle<CachedData> updateHandle)
	{
		_updateHandle = updateHandle;
	}

	public Task<UpdateResult<IPlayerData>> SetUnviewedDocuments(List<Document> docs)
	{
		return _updateHandle.Update<IPlayerData>(delegate(CachedData state)
		{
			state.UnviewedDocuments = docs;
		});
	}

	public Task<UpdateResult<IPlayerData>> RemoveDocument(string title, string type)
	{
		return _updateHandle.Update<IPlayerData>(delegate(CachedData state)
		{
			if (state.UnviewedDocuments.Count > 0)
			{
				state.UnviewedDocuments.RemoveAll((Document r) => r.Title == title && r.Type == type);
			}
		});
	}

	public Task<UpdateResult<IPlayerData>> SetLoggedIn(string userId)
	{
		return _updateHandle.Update<IPlayerData>(delegate(CachedData state)
		{
			state.UserId = userId;
			state.LoggedIn = true;
			state.UnviewedDocuments = new List<Document>();
		});
	}

	public Task<UpdateResult<IPlayerData>> SetLoggedOut(bool hardClear = false)
	{
		return _updateHandle.Update<IPlayerData>(delegate(CachedData state)
		{
			state.LoggedIn = false;
			state.UnviewedDocuments = new List<Document>();
			if (hardClear)
			{
				state.UserId = null;
				state.ThirdPartyAutoLoginPromptState = AutoLoginPromptState.PromptNotShown;
			}
		});
	}

	public Task<UpdateResult<IPlayerData>> SetPseudonymizedUserId(string pseudonymizedUserId, string idType)
	{
		return _updateHandle.Update<IPlayerData>(delegate(CachedData state)
		{
			if (state.PseudonymizedUserData == null)
			{
				state.PseudonymizedUserData = new PseudonymizedUserData();
			}
			state.PseudonymizedUserData.PseudonymizedUserId = pseudonymizedUserId;
			state.PseudonymizedUserData.IdType = idType;
		});
	}

	public Task<UpdateResult<IPlayerData>> SetThirdPartyAutoLoginPromptState(AutoLoginPromptState promptState)
	{
		return _updateHandle.Update<IPlayerData>(delegate(CachedData state)
		{
			state.ThirdPartyAutoLoginPromptState = promptState;
		});
	}
}
