using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class SearchData
{
	public string searchQuery { get; set; }

	public string author { get; set; }

	public string requiredVersion { get; set; }

	public List<string> tags { get; set; }

	public int? page { get; set; }

	public int? pageSize { get; set; }

	public SortMethod sortBy { get; set; } = SortMethod.Best;

	public SearchOrder orderBy { get; set; }

	public SearchTime? time { get; set; }

	public bool? onlyModsByMe { get; set; }

	public List<ModState> states { get; set; }
}
