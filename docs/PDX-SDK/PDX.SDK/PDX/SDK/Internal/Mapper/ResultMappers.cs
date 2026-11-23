using PDX.SDK.Contracts;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Mapper;

internal static class ResultMappers
{
	public static Result ToResult(this FileIOStatus fileIoStatus)
	{
		return new Result
		{
			Success = fileIoStatus.Success,
			Error = (fileIoStatus.Success ? null : new Error(fileIoStatus.ErrorCode.ToInternalError(), fileIoStatus.Message, string.Empty))
		};
	}

	public static T ToResult<T>(this FileIOStatus fileIoStatus) where T : Result
	{
		return fileIoStatus.ToResult().CastTo<T>();
	}
}
