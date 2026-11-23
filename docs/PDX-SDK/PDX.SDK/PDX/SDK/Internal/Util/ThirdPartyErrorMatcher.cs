using System;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Network.Util;

namespace PDX.SDK.Internal.Util;

internal static class ThirdPartyErrorMatcher
{
	internal static bool Match(Provider provider, out string outErrorMessage, bool isProviderCorrect = true, bool isConfigCorrect = true, bool isUserActionRequired = false, bool isRequestSuccessful = true, string requestErrorMessage = null, Exception otherException = null)
	{
		if (isProviderCorrect && isRequestSuccessful && otherException == null && isConfigCorrect && !isUserActionRequired)
		{
			outErrorMessage = null;
			return false;
		}
		Enum thirdPartyErrorSubCategory = GetThirdPartyErrorSubCategory(provider, isConfigCorrect, isUserActionRequired);
		string thirdPartyErrorMessage = GetThirdPartyErrorMessage(provider, requestErrorMessage, otherException);
		outErrorMessage = thirdPartyErrorSubCategory.GenerateErrorBody(thirdPartyErrorMessage);
		return true;
	}

	private static Enum GetThirdPartyErrorSubCategory(Provider provider, bool isConfigCorrect, bool isUserActionRequired)
	{
		switch (provider)
		{
		case Provider.PSN:
			return Psn.TokenFetchError;
		case Provider.XboxLive:
			return Xbox.TokenFetchError;
		case Provider.Google:
			if (!isConfigCorrect)
			{
				return GoogleAndroid.ClientIdRequired;
			}
			if (isUserActionRequired)
			{
				return GoogleAndroid.UserSigninRequired;
			}
			return GoogleAndroid.TokenFetchError;
		case Provider.Steam:
			return Steam.InvalidCredentials;
		default:
			return InternalError.ThirdPartyException;
		}
	}

	private static string GetThirdPartyErrorMessage(Provider provider, string requestErrorMessage, Exception otherException)
	{
		string provider2 = provider.GetProvider();
		string text = "Cannot automatically retrieve " + provider2 + " credentials";
		if (!string.IsNullOrEmpty(requestErrorMessage))
		{
			text = text + ": " + requestErrorMessage;
		}
		if (otherException != null)
		{
			text = text + ": " + otherException.Message;
		}
		return text;
	}
}
