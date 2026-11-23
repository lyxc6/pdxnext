using PDX.SDK.Contracts;

namespace PDX.SDK.Internal.Util;

internal static class ResultExtensions
{
	internal static T CastTo<T>(this Result result) where T : Result
	{
		return new T
		{
			Error = result.Error,
			RequestId = result.RequestId,
			Success = result.Success
		};
	}
}
