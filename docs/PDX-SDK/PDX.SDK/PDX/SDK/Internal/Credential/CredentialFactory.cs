using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Internal.Credential;

internal class CredentialFactory : ICredentialFactory
{
	private readonly IContext _context;

	public CredentialFactory(IContext context)
	{
		_context = context;
	}

	public virtual ICredential GetEnabledThirdPartyCredential()
	{
		if (_context.Config.ThirdParty.Psn.Enabled == true)
		{
			return new PsnCredential();
		}
		if (_context.Config.ThirdParty.Xbox.Enabled == true)
		{
			return new XboxLiveCredential();
		}
		if (_context.Config.ThirdParty.Nintendo.Enabled == true)
		{
			return new NintendoAccountCredential();
		}
		if (_context.Config.ThirdParty.Apple.Enabled == true)
		{
			return new AppleSignInCredential();
		}
		if (_context.Config.ThirdParty.Google.Enabled == true)
		{
			return new GoogleCredential();
		}
		if (_context.Config.ThirdParty.Stadia.Enabled == true)
		{
			return new StadiaCredential();
		}
		if (_context.Config.ThirdParty.Steam.Enabled == true)
		{
			if (string.IsNullOrEmpty(_context.Config.ThirdParty.Steam.Ticket))
			{
				_context.Config.Logger.Log("Steam Ticket is not set up", LogLevel.L2_Warning);
				return null;
			}
			if (!_context.Config.ThirdParty.Steam.AppId.HasValue)
			{
				_context.Config.Logger.Log("Steam App ID is not configured", LogLevel.L2_Warning);
				return null;
			}
			return new SteamCredential(_context.Config.ThirdParty.Steam.Ticket, _context.Config.ThirdParty.Steam.AppId.GetValueOrDefault());
		}
		return null;
	}
}
