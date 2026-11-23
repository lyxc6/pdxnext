using System.Collections.Generic;
using System.Linq;

namespace PDX.SDK.Internal.Util;

public static class EnumerableExtensions
{
	public static IEnumerable<T> GetPage<T>(this IOrderedEnumerable<T> valuesToPaginate, int page, int size)
	{
		int num = ((page > 0) ? (page - 1) : 0);
		return valuesToPaginate.Skip(num * size).Take(size).ToList();
	}

	public static IEnumerable<T> GetPage<T>(this IEnumerable<T> valuesToPaginate, int page, int size)
	{
		int num = ((page > 0) ? (page - 1) : 0);
		return valuesToPaginate.Skip(num * size).Take(size).ToList();
	}
}
