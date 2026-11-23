using PDX.SDK.Contracts;
using PDX.SDK.Internal.Platypatch.Models;

namespace PDX.SDK.Internal.Platypatch.Results;

internal class DownloadPatchZipResult : Result
{
	public FolderManifest PatchManifest { get; set; }

	public string ZipFilePath { get; set; }
}
