using System;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Contracts.Service.Account.Result;

[Preserve]
public class GetDetailsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string Email { get; set; }

	[Preserve]
	public bool IsEmailVerified { get; set; }

	[Preserve]
	public DateTime? CreationDate { get; set; }

	[Preserve]
	public string Referer { get; set; }

	[Preserve]
	public string Source { get; set; }

	[Preserve]
	public Language Language { get; set; }

	[Preserve]
	public Country Country { get; set; }

	[Preserve]
	public DateTime? DateOfBirth { get; set; }

	[Preserve]
	public string FirstName { get; set; }

	[Preserve]
	public string LastName { get; set; }

	[Preserve]
	public string AddressLine1 { get; set; }

	[Preserve]
	public string AddressLine2 { get; set; }

	[Preserve]
	public string City { get; set; }

	[Preserve]
	public string State { get; set; }

	[Preserve]
	public string ZipCode { get; set; }
}
