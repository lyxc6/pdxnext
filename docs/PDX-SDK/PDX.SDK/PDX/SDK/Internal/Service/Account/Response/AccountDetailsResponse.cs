using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Account.Response;

[Preserve]
internal class AccountDetailsResponse
{
	[Preserve]
	public string firstName { get; set; }

	[Preserve]
	public string lastName { get; set; }

	[Preserve]
	public string addressLine1 { get; set; }

	[Preserve]
	public string addressLine2 { get; set; }

	[Preserve]
	public string city { get; set; }

	[Preserve]
	public string state { get; set; }

	[Preserve]
	public string zipCode { get; set; }

	[Preserve]
	public string country { get; set; }

	[Preserve]
	public string language { get; set; }

	[Preserve]
	public string dateOfBirth { get; set; }
}
