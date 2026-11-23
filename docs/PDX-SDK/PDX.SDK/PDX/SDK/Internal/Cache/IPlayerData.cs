using System.Collections.Generic;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Legal.Models;
using PDX.SDK.Internal.Cache.Models;

namespace PDX.SDK.Internal.Cache;

internal interface IPlayerData
{
	string UserId { get; }

	bool LoggedIn { get; }

	List<Document> UnviewedDocuments { get; }

	AutoLoginPromptState ThirdPartyAutoLoginPromptState { get; }

	PseudonymizedUserData PseudonymizedUserData { get; }
}
