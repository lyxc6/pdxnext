using System;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Service.Account.Request;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Service.Account.Models;

internal class CreateAccountInputData
{
	internal EmailAndPasswordCredential EmailAndPassword { get; set; }

	internal Language Language { get; set; }

	internal Country Country { get; set; }

	internal DateTime DateOfBirth { get; set; }

	internal string Source { get; set; }

	internal string Referer { get; set; }

	internal string SourceService { get; set; }

	internal string Campaign { get; set; }

	internal string Medium { get; set; }

	internal string Channel { get; set; }

	internal string FirstName { get; set; }

	internal string LastName { get; set; }

	internal string AddressLine1 { get; set; }

	internal string AddressLine2 { get; set; }

	internal string City { get; set; }

	internal string State { get; set; }

	internal string ZipCode { get; set; }

	internal string EmailTemplate { get; set; }

	internal string LandingUrl { get; set; }

	internal string RefererAccount { get; set; }

	internal bool MarketingPermission { get; set; }

	internal AccountCreateDescRequest ToAccountCreateDescRequest()
	{
		return new AccountCreateDescRequest
		{
			email = EmailAndPassword.Email,
			password = EmailAndPassword.Password,
			source = Source,
			referer = Referer,
			sourceService = SourceService,
			campaign = Campaign,
			medium = Medium,
			channel = Channel,
			details = new AccountCreateDetailsRequest
			{
				language = Language.ToIetfTagLanguageString(),
				country = Country.ToString(),
				dateOfBirth = DateOfBirth.ToString("yyyy-MM-dd", Defaults.Culture),
				firstName = FirstName,
				lastName = LastName,
				addressLine1 = AddressLine1,
				addressLine2 = AddressLine2,
				city = City,
				state = State,
				zipCode = ZipCode
			},
			options = new AccountCreateOptionsRequest
			{
				emailTemplate = EmailTemplate,
				landingUrl = LandingUrl,
				refererAccount = RefererAccount,
				marketingPermission = MarketingPermission
			}
		};
	}
}
