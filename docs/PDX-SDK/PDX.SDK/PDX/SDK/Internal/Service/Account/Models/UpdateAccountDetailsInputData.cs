using System;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Service.Account.Request;

namespace PDX.SDK.Internal.Service.Account.Models;

internal class UpdateAccountDetailsInputData
{
	internal Language? Language { get; set; }

	internal Country? Country { get; set; }

	internal DateTime? DateOfBirth { get; set; }

	internal string FirstName { get; set; }

	internal string LastName { get; set; }

	internal string AddressLine1 { get; set; }

	internal string AddressLine2 { get; set; }

	internal string City { get; set; }

	internal string State { get; set; }

	internal string ZipCode { get; set; }

	internal AccountCreateDetailsRequest ToAccountCreateDetailsRequest()
	{
		return new AccountCreateDetailsRequest
		{
			language = Language?.ToString(),
			country = Country?.ToString(),
			dateOfBirth = DateOfBirth?.ToString("yyyy-MM-dd", Defaults.Culture),
			firstName = FirstName,
			lastName = LastName,
			addressLine1 = AddressLine1,
			addressLine2 = AddressLine2,
			city = City,
			state = State,
			zipCode = ZipCode
		};
	}
}
