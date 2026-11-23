using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class UpdateWipData
{
	public string guid { get; set; }

	public string displayName { get; set; }

	public string contentFileOrFolderName { get; set; }

	public string shortDescription { get; set; }

	public string longDescription { get; set; }

	public ulong size { get; set; }

	public string thumbnailFilename { get; set; }

	public List<string> screenshotsFilenames { get; set; }

	public List<ModTag> tags { get; set; }
}
