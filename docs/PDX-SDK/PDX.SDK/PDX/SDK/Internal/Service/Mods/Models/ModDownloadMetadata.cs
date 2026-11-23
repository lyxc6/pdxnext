using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods.Models;

internal class ModDownloadMetadata
{
	public int Id { get; set; }

	public string Version { get; set; }

	public string Name { get; set; }

	public string RepositoryPath { get; set; }

	public string ThumbnailPath { get; set; }

	public List<Screenshot> Screenshots { get; set; }

	public ulong Size { get; set; }
}
