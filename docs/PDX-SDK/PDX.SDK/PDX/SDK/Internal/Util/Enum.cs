using System;

namespace PDX.SDK.Internal.Util;

internal static class Enum<T> where T : struct, IConvertible
{
	internal static T ParseOrDefault(string value, bool ignoreCase = true)
	{
		if (!Enum.TryParse<T>(value, ignoreCase, out var result))
		{
			return default(T);
		}
		return result;
	}
}
