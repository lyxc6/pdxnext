using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ActionsResponse
{
	[Preserve]
	public bool canEdit { get; set; }

	[Preserve]
	public bool canInstall { get; set; }

	[Preserve]
	public bool canEnable { get; set; }

	[Preserve]
	public bool canRate { get; set; }

	[Preserve]
	public bool canUpload { get; set; }

	[Preserve]
	public bool canDownload { get; set; }

	[Preserve]
	public bool canViewDisabled { get; set; }

	[Preserve]
	public bool canSelectVersion { get; set; }

	[Preserve]
	public bool canReorder { get; set; }

	[Preserve]
	public bool activationSets { get; set; }
}
