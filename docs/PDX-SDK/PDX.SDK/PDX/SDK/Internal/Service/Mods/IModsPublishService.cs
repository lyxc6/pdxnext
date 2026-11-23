using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal interface IModsPublishService
{
	Task<PublishResult> PublishWIP(PublishWipData publishWipData, FlowData flowData = null);

	Task<PublishResult> PublishV2WIP(PublishWipData publishWipData, FlowData flowData = null);

	Task<PublishUpdateResult> PublishUpdateV1(PublishUpdateData publishUpdateData, bool isNewVersion = false, FlowData flowData = null);

	Task<PublishUpdateResult> PublishNewModVersionV2(PublishUpdateData publishNewModVersionData, FlowData flowData = null);

	Task<PublishUpdateResult> UpdatePublishedModVersionV2(PublishUpdateData publishUpdateData, string version, FlowData flowData = null);

	Task<Result> Unpublish(int modId, FlowData flowData = null);
}
