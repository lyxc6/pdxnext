using System;
using System.Globalization;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.StandardConstantValues;

namespace PDX.SDK.Internal.Util;

internal static class HelperExtensionMethods
{
	internal static void Forget(this Task task)
	{
		task.ContinueWith(delegate
		{
		}, TaskContinuationOptions.OnlyOnFaulted);
	}

	internal static bool ParseBool(this string value)
	{
		if (value != null)
		{
			if (!(value == "true"))
			{
				return value == "1";
			}
			return true;
		}
		return false;
	}

	internal static DateTime? ParseDateTime(this string input, string functionName, string timeDescriptor, string serviceName, ILogger logger = null, FlowData fd = null)
	{
		if (input == null)
		{
			return null;
		}
		if (DateTime.TryParse(input, Defaults.Culture, DateTimeStyles.None, out var result))
		{
			return result;
		}
		logger?.Log(string.Format(Defaults.Culture, "Error parsing {0} in {1}.{2} result. Invalid format: {3}", timeDescriptor, serviceName, functionName, input), LogLevel.L3_Error, fd);
		return null;
	}

	internal static int ParseIntOrDefault(this string value, int defaultValue = -1)
	{
		if (!int.TryParse(value, out var result))
		{
			return defaultValue;
		}
		return result;
	}
}
