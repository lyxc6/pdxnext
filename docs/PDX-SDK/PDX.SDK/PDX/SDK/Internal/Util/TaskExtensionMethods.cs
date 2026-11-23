using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;

namespace PDX.SDK.Internal.Util;

internal static class TaskExtensionMethods
{
	internal static async Task<Result> GetResultFromTasks(this IEnumerable<Task<Result>> tasks)
	{
		return (await Task.WhenAll(tasks).ConfigureAwait(continueOnCapturedContext: false)).FirstOrDefault((Result r) => !r.Success) ?? new Result
		{
			Success = true
		};
	}
}
