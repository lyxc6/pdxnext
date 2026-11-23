using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class SearchRequest
{
	[Preserve]
	public string[] osList;

	[Preserve]
	public string arch;

	[Preserve]
	public string[] gameNames;

	[Preserve]
	public string searchQuery;

	[Preserve]
	public string author;

	[Preserve]
	public string requiredVersion;

	[Preserve]
	public string[] tags;

	[Preserve]
	public int? page;

	[Preserve]
	public int? pageSize;

	[Preserve]
	public string sortBy;

	[Preserve]
	public string orderBy;

	[Preserve]
	public string time;

	[Preserve]
	public bool? onlyModsByMe;

	[Preserve]
	public bool? onlyInstalled;

	[Preserve]
	public int? activationSetId;

	[Preserve]
	public bool? includeDependants;

	[Preserve]
	public string[] states;
}
