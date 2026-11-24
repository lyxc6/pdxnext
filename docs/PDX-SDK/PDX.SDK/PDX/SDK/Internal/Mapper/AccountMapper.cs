using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Service.Account.Result;
using PDX.SDK.Internal.Service.Account.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Mapper;

internal static class AccountMapper
{
	internal static GetDetailsResult FromGetDetailsResponse(this GetDetailsResult details, GetDetailsResponse response, ILogger logger = null, FlowData fd = null)
	{
		if (response.account == null)
		{
			return details;
		}
		details.Email = ulp(response.account.email);
		details.IsEmailVerified = response.account.isEmailVerified;
		details.CreationDate = ulp(response.account.creationDate).ParseDateTime("creation date", "GetDetailsResult", "AccountMapper", logger, fd);
		details.Referer = ulp(response.account.referer);
		details.Source = ulp(response.account.source);
		if (response.accountDetails == null)
		{
			return details;
		}
		details.Language = IsoExtensionMethods.GetLanguage(ulp(response.accountDetails.language));
		details.Country = IsoExtensionMethods.GetCountry(ulp(response.accountDetails.country));
		details.FirstName = ulp(response.accountDetails.firstName);
		details.LastName = ulp(response.accountDetails.lastName);
		details.AddressLine1 = ulp(response.accountDetails.addressLine1);
		details.AddressLine2 = ulp(response.accountDetails.addressLine2);
		details.City = ulp(response.accountDetails.city);
		details.State = ulp(response.accountDetails.state);
		details.ZipCode = ulp(response.accountDetails.zipCode);
		details.DateOfBirth = ulp(response.accountDetails.dateOfBirth).ParseDateTime("date of birth", "GetDetailsResult", "AccountMapper", logger, fd);
		return details;
		static string ulp(string input)
		{
			if (!string.IsNullOrWhiteSpace(input) && !string.IsNullOrEmpty(input) && !(input == "ul"))
			{
				return input;
			}
			return null;
		}
	}
}
