using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Internal.Service.Mods.Enums;

namespace PDX.SDK.Internal.Service.Mods.Helpers;

internal static class EnumConversionExtensions
{
	private static Dictionary<SortMethod, string> sortKVs = new Dictionary<SortMethod, string>
	{
		{
			SortMethod.Best,
			"best"
		},
		{
			SortMethod.DisplayName,
			"displayName"
		},
		{
			SortMethod.Created,
			"created"
		},
		{
			SortMethod.Updated,
			"updated"
		},
		{
			SortMethod.Rating,
			"rating"
		},
		{
			SortMethod.Popularity,
			"popularity"
		},
		{
			SortMethod.ActivationOrder,
			"activationOrder"
		}
	};

	private static Dictionary<string, ModState> statesKVs = new Dictionary<string, ModState>
	{
		{
			"published",
			ModState.Published
		},
		{
			"publishing",
			ModState.Publishing
		},
		{
			"disabledByManager",
			ModState.Disabled
		},
		{
			"removedByUser",
			ModState.Removed
		},
		{
			"rejectedVirus",
			ModState.Rejected
		},
		{
			"autoBlocked",
			ModState.AutoBlocked
		}
	};

	private static Dictionary<SearchTime, string> timeKVs = new Dictionary<SearchTime, string>
	{
		{
			SearchTime.AllTime,
			"alltime"
		},
		{
			SearchTime.Day,
			"day"
		},
		{
			SearchTime.Week,
			"week"
		},
		{
			SearchTime.Month,
			"month"
		},
		{
			SearchTime.Quarter,
			"quarter"
		},
		{
			SearchTime.SixMonths,
			"sixmonths"
		},
		{
			SearchTime.Year,
			"year"
		}
	};

	private static Dictionary<string, LocalType> localtypeKVs = new Dictionary<string, LocalType>
	{
		{
			"Subscribed",
			LocalType.Subscribed
		},
		{
			"WorkInProgress",
			LocalType.WorkInProgress
		},
		{
			"Unmanaged",
			LocalType.Unmanaged
		}
	};

	internal static string GetFileType(this FileType type)
	{
		return type switch
		{
			FileType.Thumbnail => "thumbnail", 
			FileType.Screenshot => "screenshot", 
			FileType.Content => "content", 
			_ => "", 
		};
	}

	internal static string GetSortString(this SortMethod method)
	{
		if (sortKVs.ContainsKey(method))
		{
			return sortKVs[method];
		}
		return "";
	}

	internal static string GetSearchOrderString(this SearchOrder order)
	{
		return order switch
		{
			SearchOrder.Ascending => "asc", 
			SearchOrder.Descending => "desc", 
			_ => "", 
		};
	}

	internal static string GetTimeString(this SearchTime searchTime)
	{
		if (timeKVs.ContainsKey(searchTime))
		{
			return timeKVs[searchTime];
		}
		return "";
	}

	internal static ModState GetModState(this string state)
	{
		if (state == null)
		{
			return ModState.Unknown;
		}
		if (statesKVs.ContainsKey(state))
		{
			return statesKVs[state];
		}
		return ModState.Unknown;
	}

	internal static string GetModState(this ModState state)
	{
		string result = "published";
		foreach (KeyValuePair<string, ModState> statesKV in statesKVs)
		{
			if (statesKV.Value == state)
			{
				return statesKV.Key;
			}
			if (statesKV.Value == ModState.Published)
			{
				result = statesKV.Key;
			}
		}
		return result;
	}

	public static LocalType GetLocalType(this string type)
	{
		if (type == null || !localtypeKVs.ContainsKey(type))
		{
			return LocalType.Subscribed;
		}
		return localtypeKVs[type];
	}

	public static string GetLocalType(this LocalType type)
	{
		foreach (KeyValuePair<string, LocalType> localtypeKV in localtypeKVs)
		{
			if (localtypeKV.Value == type)
			{
				return localtypeKV.Key;
			}
		}
		return "Subscribed";
	}

	public static DependencyType GetDependencyType(this string type)
	{
		if (type.Equals("DLC", StringComparison.OrdinalIgnoreCase))
		{
			return DependencyType.Dlc;
		}
		if (type.Equals("MOD", StringComparison.OrdinalIgnoreCase))
		{
			return DependencyType.Mod;
		}
		return DependencyType.Unknown;
	}

	internal static string GetReportReasonMod(this ReportReason reason)
	{
		return reason switch
		{
			ReportReason.Copyright => "copyright", 
			ReportReason.Nudity => "nudity", 
			ReportReason.Violence => "violence", 
			ReportReason.Harassment => "harassment", 
			ReportReason.Spam => "spam", 
			ReportReason.Hate => "hate", 
			ReportReason.DoesNotWork => "doesnotwork", 
			_ => "other", 
		};
	}

	internal static string GetReportReasonModCreator(this ReportReason reason)
	{
		return reason switch
		{
			ReportReason.Copyright => "copyright", 
			ReportReason.Violence => "violence", 
			ReportReason.Harassment => "harassment", 
			ReportReason.Spam => "spam", 
			ReportReason.Hate => "hate", 
			_ => "other", 
		};
	}
}
