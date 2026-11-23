using System;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Account.Result;

namespace PDX.SDK.Contracts.Service.Account;

public interface IAccountService
{
	Task<CreateResult> Create(EmailAndPasswordCredential emailAndPassword, Language language, Country country, DateTime dateOfBirth);

	Task<CreateResult> Create(EmailAndPasswordCredential emailAndPassword, Language language, Country country, DateTime dateOfBirth, string source = null, string referer = null, string sourceService = null, string campaign = null, string medium = null, string channel = null, string firstName = null, string lastName = null, string addressLine1 = null, string addressLine2 = null, string city = null, string state = null, string zipCode = null, string emailTemplate = null, string landingUrl = null, string refererAccount = null, bool marketingPermission = false);

	Task<PDX.SDK.Contracts.Result> UpdateDetails(Language? language = null, Country? country = null, DateTime? dateOfBirth = null, string firstName = null, string lastName = null, string addressLine1 = null, string addressLine2 = null, string city = null, string state = null, string zipCode = null);

	Task<GetDetailsResult> GetDetails();

	Task<PDX.SDK.Contracts.Result> ReportPlayer(string reportedAccountId, string category);

	Task<ExistsResult> Exists(ICredential credential);

	Task<LoginResult> Login(ICredential credential);

	Task<PDX.SDK.Contracts.Result> Logout();

	Task<PDX.SDK.Contracts.Result> ResetPassword(string email);

	Task<StartupResult> Startup();
}
