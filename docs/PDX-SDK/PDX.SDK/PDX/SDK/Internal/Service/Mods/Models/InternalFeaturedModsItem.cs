namespace PDX.SDK.Internal.Service.Mods.Models;

public class InternalFeaturedModsItem
{
	public int id { get; set; }

	public string os { get; set; }

	public string name { get; set; }

	public ulong size { get; set; }

	public string author { get; set; }

	public int? rating { get; set; }

	public int ratingsTotal { get; set; }

	public string displayName { get; set; }

	public string editorTitle { get; set; }

	public string latestVersion { get; set; }

	public ulong installedCount { get; set; }

	public string shortDescription { get; set; }

	public string editorDescription { get; set; }

	public string cover { get; set; }
}
