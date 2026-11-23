using System.Collections.Generic;
using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Util;

public static class TrustLevelHelper
{
	private static Dictionary<string, TrustLevel> levelKVs = new Dictionary<string, TrustLevel>
	{
		{
			"Undefined",
			TrustLevel.Undefined
		},
		{
			"LimitedWrite",
			TrustLevel.Refresh
		},
		{
			"ThirdParty",
			TrustLevel.ThirdParty
		},
		{
			"Maximum",
			TrustLevel.Full
		}
	};

	public static TrustLevel GetTrustLevel(string level)
	{
		if (level == null)
		{
			return TrustLevel.Undefined;
		}
		if (levelKVs.ContainsKey(level))
		{
			return levelKVs[level];
		}
		return TrustLevel.Undefined;
	}

	public static string GetTrustLevel(this TrustLevel level)
	{
		string result = "Undefined";
		foreach (KeyValuePair<string, TrustLevel> levelKV in levelKVs)
		{
			if (level == levelKV.Value)
			{
				return levelKV.Key;
			}
			if (levelKV.Value == TrustLevel.Undefined)
			{
				result = levelKV.Key;
			}
		}
		return result;
	}
}
