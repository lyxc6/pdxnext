using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.DataStorage.CloudSave.Models;
using PDX.SDK.Contracts.Service.DataStorage.CloudSave.Result;

namespace PDX.SDK.Contracts.Service.DataStorage.CloudSave;

public interface ICloudSaveService
{
	Task<PDX.SDK.Contracts.Result> Delete(Entry entry);

	Task<PDX.SDK.Contracts.Result> Delete(IEnumerable<Entry> entries);

	Task<PDX.SDK.Contracts.Result> Delete(params string[] remotePaths);

	Task<PDX.SDK.Contracts.Result> Delete(string remotePath);

	Task<DownloadResult> Download(string remotePath, string relativePath, string etag = null);

	Task<ListEntriesResult> ListEntries();

	Task<UploadResult> Upload(string relativePath, string remotePath, bool @public = false);
}
