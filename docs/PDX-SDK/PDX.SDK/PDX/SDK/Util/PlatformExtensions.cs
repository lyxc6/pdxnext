using System.Collections.Generic;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Util;

public static class PlatformExtensions
{
	private static Dictionary<ModPlatform, string> modsPlatformStrings = new Dictionary<ModPlatform, string>
	{
		{
			ModPlatform.Any,
			"Any"
		},
		{
			ModPlatform.Windows,
			"Windows"
		},
		{
			ModPlatform.Linux,
			"Linux"
		},
		{
			ModPlatform.Osx,
			"OSX"
		},
		{
			ModPlatform.Playstation5,
			"Playstation_5"
		},
		{
			ModPlatform.XboxOne,
			"Xbox_one"
		},
		{
			ModPlatform.XboxSeriesXS,
			"Xbox_series_xs"
		}
	};

	internal static string GetPlatform(this Platform plat)
	{
		return plat switch
		{
			Platform.Windows => "windows", 
			Platform.Linux => "linux", 
			Platform.MacOS => "osx", 
			Platform.iPhoneOS_Handheld => "ios_handheld", 
			Platform.iPhoneOS_Tablet => "ios_tablet", 
			Platform.Android_Handheld => "android_handheld", 
			Platform.Android_Tablet => "android_tablet", 
			Platform.Others_Handheld => "others_handheld", 
			Platform.Others_Tablet => "others_tablet", 
			Platform.Playstation4 => "ps4", 
			Platform.Playstation5 => "ps5", 
			Platform.XboxOne => "xbox_one", 
			Platform.XboxSeriesXS => "xbox_series_xs", 
			Platform.Switch => "switch", 
			Platform.Streaming => "streaming", 
			_ => string.Empty, 
		};
	}

	internal static bool IsValidModsConsolePlatform(this Platform platform)
	{
		if (platform != Platform.XboxOne && platform != Platform.XboxSeriesXS)
		{
			return platform == Platform.Playstation5;
		}
		return true;
	}

	internal static ModPlatform GetModsPlatform(this Platform platform)
	{
		return platform switch
		{
			Platform.Windows => ModPlatform.Windows, 
			Platform.Linux => ModPlatform.Linux, 
			Platform.MacOS => ModPlatform.Osx, 
			Platform.XboxOne => ModPlatform.XboxOne, 
			Platform.XboxSeriesXS => ModPlatform.XboxSeriesXS, 
			Platform.Playstation5 => ModPlatform.Playstation5, 
			_ => ModPlatform.Any, 
		};
	}

	internal static string ToModsPlatformString(this ModPlatform plat)
	{
		if (modsPlatformStrings.ContainsKey(plat))
		{
			return modsPlatformStrings[plat];
		}
		return plat.ToString();
	}
}
