using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Legal.Models;
using PDX.SDK.Internal.Cache.Models;

namespace PDX.SDK.Internal.Cache;

internal interface IPlayerCommand
{
	Task<UpdateResult<IPlayerData>> SetUnviewedDocuments(List<Document> docs);

	Task<UpdateResult<IPlayerData>> RemoveDocument(string title, string type);

	Task<UpdateResult<IPlayerData>> SetLoggedIn(string userId);

	Task<UpdateResult<IPlayerData>> SetLoggedOut(bool hardClear = false);

	Task<UpdateResult<IPlayerData>> SetPseudonymizedUserId(string pseudonymizedUserId, string idType);

	Task<UpdateResult<IPlayerData>> SetThirdPartyAutoLoginPromptState(AutoLoginPromptState promptState);
}
