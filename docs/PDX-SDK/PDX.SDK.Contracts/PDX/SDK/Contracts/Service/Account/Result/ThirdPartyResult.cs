using System.Collections.Generic;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Models;

namespace PDX.SDK.Contracts.Service.Account.Result;

public class ThirdPartyResult
{
	public List<AccountLink> ParadoxAccountLinks { get; set; }

	public List<AccountLink> ThirdPartyAccountLinks { get; set; }

	public bool IsLinkedTo(Provider provider)
	{
		foreach (AccountLink thirdPartyAccountLink in ThirdPartyAccountLinks)
		{
			if (thirdPartyAccountLink.Provider == provider)
			{
				return true;
			}
		}
		return false;
	}

	public ThirdPartyResult()
	{
		ParadoxAccountLinks = new List<AccountLink>();
		ThirdPartyAccountLinks = new List<AccountLink>();
	}
}
