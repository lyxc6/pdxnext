namespace PDX.SDK.Contracts.Service.Mods.Models;

public class FeaturedModsItem
{
	public int Id { get; set; }

	public string Name { get; set; }

	public ulong Size { get; set; }

	public string Author { get; set; }

	public int? Rating { get; set; }

	public int RatingsTotal { get; set; }

	public string DisplayName { get; set; }

	public string EditorTitle { get; set; }

	public string LatestVersion { get; set; }

	public ulong InstalledCount { get; set; }

	public string ShortDescription { get; set; }

	public string EditorDescription { get; set; }

	public string Cover { get; set; }
}
