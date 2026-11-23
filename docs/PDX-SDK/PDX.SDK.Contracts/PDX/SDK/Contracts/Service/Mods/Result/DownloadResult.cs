using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class DownloadResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string ContentFileOrFolder { get; set; }

	[Preserve]
	public string ThumbnailFilename { get; set; }

	[Preserve]
	public List<string> ScreenshotsFilenames { get; set; }

	[Preserve]
	public DownloadResult()
	{
		ContentFileOrFolder = null;
		ThumbnailFilename = null;
		ScreenshotsFilenames = new List<string>();
	}
}
