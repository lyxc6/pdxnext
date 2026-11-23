using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts;

[Preserve]
public class AggregateResult : Result
{
	private readonly List<Result> _subResults = new List<Result>();

	public IEnumerable<Result> SubResults => _subResults;

	public AggregateResult()
	{
		base.Success = true;
	}

	public void Add(Result result)
	{
		if (base.Success && !result.Success)
		{
			base.Success = false;
			base.Error = result.Error;
		}
		_subResults.Add(result);
	}
}
