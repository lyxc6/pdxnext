using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Platypatch.Models;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal class FileSystemOperation : IFileSystemOperation
{
	public Result CreateSymlink(string target, string linkName, FlowData flowData = null, ILogger logger = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		logger?.Log("CreateSymlink was called on non supported platform, doing nothing", LogLevel.L0_Info, flowData2);
		return Result.ResultSuccessful();
	}

	public Result ApplyFlags(ManifestFileEntry fileEntry, string absoluteDestination, FlowData flowData = null, ILogger logger = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		logger?.Log("ApplyFlags was called on non supported platform, doing nothing", LogLevel.L0_Info, flowData2);
		return Result.ResultSuccessful();
	}

	public Result Unlink(string path, FlowData flowData = null, ILogger logger = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		logger?.Log("Unlink was called on non supported platform, doing nothing", LogLevel.L0_Info, flowData2);
		return Result.ResultSuccessful();
	}
}
