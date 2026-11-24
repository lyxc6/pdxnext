namespace PDX.SDK.Internal.Service.Telemetry.Models;

public class ParadoxSDKInfo
{
	public string SDKversion { get; private set; }

	public ParadoxSDKInfo()
	{
		SDKversion = SDKVersion.Version;
	}
}
