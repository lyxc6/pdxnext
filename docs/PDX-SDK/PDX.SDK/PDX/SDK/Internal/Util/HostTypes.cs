using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.StandardConstantValues;

namespace PDX.SDK.Internal.Util;

internal class HostTypes
{
	public enum HostType
	{
		Default,
		None,
		Telemetry
	}

	internal static string GetHostName(BackendEnvironment? environment, ILogger log, HostType hostType = HostType.Default)
	{
		if (hostType == HostType.None)
		{
			return "";
		}
		switch (environment)
		{
		case BackendEnvironment.Local:
			return "http://127.0.0.1:8080";
		case BackendEnvironment.Test:
			if (hostType == HostType.Telemetry)
			{
				return "https://test-telemetry.paradox-interactive.com";
			}
			return "https://test-api.paradox-interactive.com";
		case BackendEnvironment.Staging:
			if (hostType == HostType.Telemetry)
			{
				return "https://staging-telemetry.paradox-interactive.com";
			}
			return "https://staging-api.paradox-interactive.com";
		case BackendEnvironment.Sandbox:
			if (hostType == HostType.Telemetry)
			{
				return "https://sandbox-telemetry.paradox-interactive.com";
			}
			return "https://sandbox-api.paradox-interactive.com";
		case BackendEnvironment.Live:
			if (hostType == HostType.Telemetry)
			{
				return "https://telemetry.paradox-interactive.com";
			}
			return "https://api.paradox-interactive.com";
		default:
			log.Log(string.Format(Defaults.Culture, "BackendEnvironment: \"{0}\", has not been implemented in PDX.SDK.InternalContext.GetHostname(HostType)", environment));
			return "";
		}
	}
}
