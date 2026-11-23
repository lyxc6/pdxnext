namespace PDX.SDK.Internal.Service.Mods.Helpers;

internal static class LocalPlaysetKeyExtensions
{
	public static string GetLocalPlaysetKey(this int id)
	{
		return "local_" + id.ToString("0000000");
	}
}
