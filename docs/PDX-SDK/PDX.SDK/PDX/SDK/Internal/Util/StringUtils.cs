using System.Text.RegularExpressions;

namespace PDX.SDK.Internal.Util;

public static class StringUtils
{
	internal static string WildCardToRegular(string value)
	{
		return "^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
	}
}
