using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums.Errors;

namespace PDX.SDK.Internal.Results;

internal static class ResultFactory
{
	public static Result CreateCancellationResult(bool success)
	{
		return new Result
		{
			Success = success,
			Error = (success ? null : new Error(Cancelled.ByUser, "Cancellation was requested by the user.", string.Empty))
		};
	}
}
