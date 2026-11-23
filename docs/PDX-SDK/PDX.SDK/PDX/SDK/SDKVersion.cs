namespace PDX.SDK;

public static class SDKVersion
{
	public static readonly string MajorVersion = "1";

	public static readonly string MinorVersion = "31";

	public static readonly string PatchVersion = "0";

	public static readonly string RevisionVersion = "2c0757536";

	public static readonly string Version = "1.31.0";

	public static readonly string BuildTime = "2025-02-04T07:18:10Z UTC";

	public static string GetVersion()
	{
		return Version;
	}

	public static string GetBuildTime()
	{
		return BuildTime;
	}

	public static string GetRevisionVersion()
	{
		return RevisionVersion;
	}
}
