using PDX.SDK.Contracts;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Errors;

internal class ModsCacheError : Result
{
	public FileIOStatus FileIoStatus { get; set; }

	public ModsCacheError()
	{
	}

	public ModsCacheError(Result result)
	{
		base.RequestId = result.RequestId;
		base.Success = result.Success;
		base.Error = result.Error;
	}
}
