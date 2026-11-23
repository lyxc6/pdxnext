using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.DataStorage.CloudSave.Response;

[Preserve]
internal class EntryResponse
{
	[Preserve]
	public string etag { get; set; }

	[Preserve]
	public bool private_edit { get; set; }

	[Preserve]
	public string path { get; set; }

	[Preserve]
	public bool @public { get; set; }

	[Preserve]
	public string user_agent { get; set; }

	[Preserve]
	public string modified { get; set; }

	[Preserve]
	public string id { get; set; }

	[Preserve]
	public bool official { get; set; }

	[Preserve]
	public string owner { get; set; }

	[Preserve]
	public int content_length { get; set; }

	[Preserve]
	public string game { get; set; }

	[Preserve]
	public string created { get; set; }
}
