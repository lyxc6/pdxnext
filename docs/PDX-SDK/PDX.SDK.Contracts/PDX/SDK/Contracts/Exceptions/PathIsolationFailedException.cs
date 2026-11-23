using System;

namespace PDX.SDK.Contracts.Exceptions;

public class PathIsolationFailedException : Exception
{
	public PathIsolationFailedException()
		: base("The SDK failed to get an isolated (per user) default root path, please set DiskIORoot in the config and re-run Context.Create")
	{
	}
}
