using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;

namespace PDX.SDK.Contracts.Logging;

[Preserve]
public interface ILogger
{
	void Log(string msg, LogLevel logLevel = LogLevel.L1_Debug, FlowData flowData = null);
}
