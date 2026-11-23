using System.Collections.Generic;
using System.Runtime.Serialization;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Legal.Models;

namespace PDX.SDK.Internal.Cache.Models;

internal class CachedData : IPlayerData, IMachineData, ISessionData
{
	public string SessionToken { get; set; }

	public string RefreshToken { get; set; }

	public TrustLevel TrustLevel { get; set; }

	public string Namespace { get; set; }

	public List<string> ViewedDocuments { get; set; } = new List<string>();

	public string UserId { get; set; }

	public bool LoggedIn { get; set; }

	[IgnoreDataMember]
	public List<Document> UnviewedDocuments { get; set; } = new List<Document>();

	public AutoLoginPromptState ThirdPartyAutoLoginPromptState { get; set; }

	public PseudonymizedUserData PseudonymizedUserData { get; set; } = new PseudonymizedUserData();
}
