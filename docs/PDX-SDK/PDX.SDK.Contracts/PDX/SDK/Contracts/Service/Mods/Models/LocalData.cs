using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class LocalData
{
	public LocalType LocalType { get; set; }

	public string FolderAbsolutePath { get; set; }

	public string ContentFileOrFolder { get; set; }

	public string ThumbnailFilename { get; set; }

	public List<string> ScreenshotsFilenames { get; set; } = new List<string>();
}
