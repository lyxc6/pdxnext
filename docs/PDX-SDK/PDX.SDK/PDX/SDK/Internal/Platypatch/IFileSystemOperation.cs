using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Platypatch.Models;

namespace PDX.SDK.Internal.Platypatch;

internal interface IFileSystemOperation
{
	Result CreateSymlink(string target, string linkName, FlowData flowData = null, ILogger logger = null);

	Result ApplyFlags(ManifestFileEntry fileEntry, string absoluteDestination, FlowData flowData = null, ILogger logger = null);

	Result Unlink(string path, FlowData flowData = null, ILogger logger = null);
}
