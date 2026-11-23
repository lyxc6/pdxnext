using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Contracts.Service.Account.Result;

[Preserve]
public class StartupResult : LoginResult
{
	[Preserve]
	public bool IsLoggedIn { get; set; }

	[Preserve]
	public AutoLoginPromptState AutoLoginPromptState { get; set; }

	[Preserve]
	public StartupResult()
	{
		IsLoggedIn = false;
		AutoLoginPromptState = AutoLoginPromptState.PromptNotShown;
	}

	[Preserve]
	public StartupResult(LoginResult login, AutoLoginPromptState autoLoginPromptState = AutoLoginPromptState.PromptNotShown)
		: base(login)
	{
		IsLoggedIn = login.Success;
		AutoLoginPromptState = autoLoginPromptState;
	}
}
