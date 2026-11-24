using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Internal.Service.Mods.Enums;
using PDX.SDK.Internal.Service.Mods.Request;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal interface IModsBackendApiService
{
	Task<Result> MarkAsDisabled(int modId, int playsetId, FlowData flowData = null);

	Task<Result> MarkAsEnabled(int modId, int playsetId, FlowData flowData = null);

	Task<Result> EnableBulk(List<int> modIds, int playsetId, FlowData flowData = null);

	Task<Result> DisableBulk(List<int> modIds, int playsetId, FlowData flowData = null);

	Task<MarkAsSubscribedResult> MarkAsSubscribed(IEnumerable<KeyValuePair<int, string>> mods, int playsetId, FlowData flowData = null);

	Task<Result> MarkAsUnsubscribed(int modId, int playsetId, FlowData flowData = null);

	Task<GetDetailsResult> GetDetails(int modId, string version = null, string os = null, FlowData flowData = null);

	Task<GetForumThreadResult> GetForumThreadResult(int modId, string modVersion, int threadId, int page, int limit, FlowData flowData = null);

	Task<CreateForumPostResult> CreateForumPost(int modId, string modVersion, int threadId, string message, FlowData flowData = null);

	Task<GetGameDataResult> GetGameData(FlowData flowData = null);

	Task<GetModsGameDataResult> GetModsGameData(FlowData flowData = null);

	Task<GetModVersionsResult> GetVersions(int modId, FlowData flowData = null);

	Task<ModSearchResult> Search(SearchData searchData, FlowData flowData = null);

	Task<Result> Report(int modId, ReportReason reason, string description, FlowData flowData = null);

	Task<RateResult> Rate(int modId, int rating, FlowData flowData = null);

	Task<GetUserRatingResult> GetUserRating(int modId, FlowData flowData = null);

	Task<ModDependenciesResult> GetAllDependencies(List<int> mods, int? playsetId = null, FlowData flowData = null);

	Task<ModDependenciesResult> GetDependencyCandidates(int modId, string searchQuery = null, int? amount = null, FlowData flowData = null);

	Task<ModDependantsResult> GetModDependants(List<int> modIds, int playsetId = 0, FlowData flowData = null);

	Task<Result> SetLoadOrder(List<ModLoadOrder> modActivationOrderList, int playsetId, FlowData flowData = null);

	Task<LoadOrderResult> ResetLoadOrder(int playsetId, FlowData flowData = null);

	Task<CreatePlaysetResult> CreatePlayset(string playsetName, FlowData flowData = null);

	Task<RenamePlaysetResult> RenamePlayset(int playsetId, string playsetName, FlowData flowData = null);

	Task<Result> DeletePlayset(int playsetId, FlowData flowData = null);

	Task<ListAllPlaysetsResult> ListAllPlaysets(FlowData flowData = null);

	Task<ListSubscribedModsInPlaysetResult> ListModsInPlayset(int playsetId, int? limit = null, int? page = null, FlowData flowData = null);

	Task<ListOfFeaturedModsResult> GetFeaturedModsLists(int? page, int? limit, FlowData flowData = null);

	Task<ListSubscribedResult> GetAllSubscribed(FlowData flowData = null);

	Task<PublishResult> Publish(PublishRequest inputData, bool v2 = false, FlowData flowData = null);

	Task<PublishUpdateResult> PublishUpdate(PublishUpdateRequest inputData, FlowData flowData = null);

	Task<PublishUpdateResult> PublishNewModVersion(PublishNewModVersionRequest inputData, int modId, FlowData flowData = null);

	Task<PublishUpdateResult> PatchModVersion(PatchModVersionRequest inputData, int modId, string version, FlowData flowData = null);

	Task<ReportModCreatorResult> ReportModCreator(string userName, ReportReason reportReason, string description, FlowData flowData = null);

	Task<GetFollowedModCreatorsResult> GetFollowedModCreators(int? limit = 20, int? page = 1, FlowData flowData = null);

	Task<GetModCreatorsProfileResult> GetModCreatorProfile(string userName, FlowData flowData = null);

	Task<Result> FollowModCreator(string userName, FlowData flowData = null);

	Task<Result> UnfollowModCreator(string userName, FlowData flowData = null);

	Task<Result> Unpublish(int modId, FlowData flowData = null);

	Task<Result> UploadFile(FileType filetype, string uuid, string absoluteFilePath, TransferStatus transferStatus, Stream fileStream, byte[] body = null, FlowData flowData = null);

	Task<Result> DeleteFile(FileType filetype, string uuid, string filePath, FlowData flowData = null);

	Task<GetPresignedUrlResult> GetPresignedUrl(string modGuid, string fileName, FlowData flowData = null);

	Task<Result> UploadFileToPresignedUrl(string url, string absolutePath, ContentType contentType, TransferStatus transferStatus, Stream fileStream, byte[] body = null, FlowData flowData = null);
}
