using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Util;

public static class EcosystemExtensions
{
	internal static string ToEcosystemString(this Ecosystem? ecosystem)
	{
		if (!ecosystem.HasValue)
		{
			return "unidentified_platform";
		}
		return ecosystem.Value switch
		{
			Ecosystem.Android => "android", 
			Ecosystem.Epic => "epic", 
			Ecosystem.Gog => "gog", 
			Ecosystem.Ios => "ios", 
			Ecosystem.Mac_AppStore => "mac_app_store", 
			Ecosystem.Nintendo => "nintendo", 
			Ecosystem.Paradox => "paradox", 
			Ecosystem.Playstation => "ps", 
			Ecosystem.Steam => "steam", 
			Ecosystem.Tencent => "tencent", 
			Ecosystem.Microsoft_Store => "microsoft", 
			Ecosystem.Xbox => "xbox", 
			_ => "unidentified_platform", 
		};
	}
}
