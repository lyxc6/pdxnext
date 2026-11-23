using System.Collections.Generic;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;

namespace PDX.SDK.Internal.Util;

internal static class ProviderExtensions
{
	internal static Dictionary<string, Provider> providers = new Dictionary<string, Provider>
	{
		{
			"psn",
			Provider.PSN
		},
		{
			"xbox",
			Provider.XboxLive
		},
		{
			"nintendo",
			Provider.NintendoAccount
		},
		{
			"apple",
			Provider.AppleSignIn
		},
		{
			"google",
			Provider.Google
		},
		{
			"stadia",
			Provider.Stadia
		},
		{
			"steam",
			Provider.Steam
		}
	};

	internal static string GetProvider(this Provider provider)
	{
		return provider switch
		{
			Provider.PSN => "PSN", 
			Provider.XboxLive => "XBoxLive", 
			Provider.Google => "Google", 
			Provider.AppleSignIn => "AppleSignIn", 
			Provider.NintendoAccount => "Nintendo", 
			Provider.Stadia => "Stadia", 
			Provider.Steam => "Steam", 
			_ => string.Empty, 
		};
	}

	internal static Provider GetProvider(string provider)
	{
		if (provider == null)
		{
			return Provider.Unknown;
		}
		if (providers.ContainsKey(provider.ToLower()))
		{
			return providers[provider];
		}
		return Provider.Unknown;
	}
}
