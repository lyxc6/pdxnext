using System;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Util.Progress;

namespace PDX.SDK.Internal.Platypatch;

internal interface IPatchManager : IDisposable
{
	Task<PatchOperation[]> GetOperations(FlowData flowData = null);

	Task<Result> ApplyOperations(PatchOperation[] operations, FolderManifest manifest, string targetAbsoluteRoot, IProgressController progressController = null, FlowData flowData = null);
}
