using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Result;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal interface IPlaysetAndModsSyncService
{
	Task<Result> Sync(SyncDirection syncDirection = SyncDirection.Default, FlowData flowData = null);

	Task<Result> SyncPlayset(int playsetId, SyncDirection syncDirection, FlowData flowData = null);

	Task<PlaysetSyncResult> AutomaticUpstreamSync(FlowData flowData = null);

	Task<PlaysetSyncConflict[]> DetectSyncConflicts(ListAllPlaysetsResult onlinePlaysets, FlowData flowData = null);

	Task<Result> CleanupReportedMods(FlowData flowData = null);
}
