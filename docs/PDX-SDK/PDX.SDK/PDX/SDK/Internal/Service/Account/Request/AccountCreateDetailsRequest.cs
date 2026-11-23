using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Account.Request;

[Preserve]
internal class AccountCreateDetailsRequest
{
	[Preserve]
	public string language;

	[Preserve]
	public string country;

	[Preserve]
	public string dateOfBirth;

	[Preserve]
	public string firstName;

	[Preserve]
	public string lastName;

	[Preserve]
	public string addressLine1;

	[Preserve]
	public string addressLine2;

	[Preserve]
	public string city;

	[Preserve]
	public string state;

	[Preserve]
	public string zipCode;
}
