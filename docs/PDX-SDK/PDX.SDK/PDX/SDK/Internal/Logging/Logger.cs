using System;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.StandardConstantValues;

namespace PDX.SDK.Internal.Logging;

internal class Logger : ILogger
{
	private readonly LogLevel _logLevel;

	private static string[] LogNames = new string[5] { "L0_Info___", "L1_Debug__", "L2_Warning", "L3_Error__", "L4_Fatal__" };

	public Logger(LogLevel? logLevel)
	{
		_logLevel = logLevel ?? LogLevel.L3_Error;
	}

	public void Log(string msg, LogLevel logLevel = LogLevel.L1_Debug, FlowData flowData = null)
	{
		if (logLevel >= _logLevel && _logLevel != LogLevel.L9_None && logLevel != LogLevel.L9_None)
		{
			msg = string.Format(Defaults.Culture, "{0} {1}", (flowData == null) ? "" : flowData.Id, msg);
			Console.WriteLine(string.Format(Defaults.Culture, "[{0}]{1}", LogNames[(int)logLevel], msg));
		}
	}
}
