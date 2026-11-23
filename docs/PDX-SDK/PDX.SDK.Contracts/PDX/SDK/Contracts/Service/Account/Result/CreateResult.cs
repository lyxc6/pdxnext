using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Account.Result;

[Preserve]
public class CreateResult : LoginResult
{
	[Preserve]
	public CreateResult()
	{
	}

	[Preserve]
	public CreateResult(LoginResult login)
		: base(login)
	{
	}
}
