using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Legal.Models;

namespace PDX.SDK.Contracts.Service.Account.Result;

[Preserve]
public class LoginResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<Document> LegalDocuments { get; set; }

	[Preserve]
	public string UserId { get; set; }

	[Preserve]
	public string SessionToken { get; set; }

	[Preserve]
	public TrustLevel TrustLevel { get; set; }

	[Preserve]
	public ThirdPartyResult ThirdParty { get; set; }

	[Preserve]
	public LoginResult()
	{
		TrustLevel = TrustLevel.Undefined;
		ThirdParty = new ThirdPartyResult();
	}

	[Preserve]
	public LoginResult(LoginResult login)
		: base(login)
	{
		LegalDocuments = login.LegalDocuments;
		UserId = login.UserId;
		SessionToken = login.SessionToken;
		TrustLevel = login.TrustLevel;
		ThirdParty = login.ThirdParty;
	}
}
