using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts;

[Preserve]
public class Result
{
	[Preserve]
	public string RequestId { get; set; }

	[Preserve]
	public bool Success { get; set; }

	[Preserve]
	public Error Error { get; set; }

	[Preserve]
	public Result()
	{
	}

	[Preserve]
	public Result(Result result)
	{
		RequestId = result.RequestId;
		Success = result.Success;
		Error = result.Error;
	}

	[Preserve]
	public static Result ResultSuccessful()
	{
		return new Result
		{
			Success = true
		};
	}
}
