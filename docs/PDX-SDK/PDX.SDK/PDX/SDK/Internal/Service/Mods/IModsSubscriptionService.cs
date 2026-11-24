using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal interface IModsSubscriptionService
{
	Task<SubscribeResult> Subscribe(IEnumerable<IMod> mods, int playsetId, FlowData flowData = null);

	Task<Result> Unsubscribe(int modId, int playsetId, FlowData flowData = null);

	Task<Result> Unsubscribe(int modId, FlowData flowData = null);

	Task<Result> Unsubscribe(string modName, int playsetId, FlowData flowData = null);

	Task<Result> Unsubscribe(string modName, FlowData flowData = null);

	Task<Result> Enable(int modId, int playsetId, bool enableOnBackend = true, FlowData flowData = null);

	Task<Result> Enable(string modName, int playsetId, FlowData flowData = null);

	Task<BulkResult> EnableBulk(List<string> modNames, int playsetId, FlowData flowData = null);

	Task<BulkResult> EnableBulk(List<int> modIds, int playsetId, FlowData flowData = null);

	Task<BulkResult> EnableBulk(int playsetId, FlowData flowData = null);

	Task<Result> Disable(int modId, int playsetId, FlowData flowData = null);

	Task<Result> Disable(string modName, int playsetId, FlowData flowData = null);

	Task<BulkResult> DisableBulk(List<int> modIds, int playsetId, FlowData flowData = null);

	Task<BulkResult> DisableBulk(List<string> modNames, int playsetId, FlowData flowData = null);

	Task<BulkResult> DisableBulk(int playsetId, FlowData flowData = null);

	Task<ListModsInPlaysetResult> ListModsInPlayset(int playsetId, int? limit = null, int? page = null, bool includeOnline = false, FlowData flowData = null);

	Task<Result> PatchMod(IMod cachedMod, IMod remoteMod, int playsetId = 0, FlowData flowData = null);
}
