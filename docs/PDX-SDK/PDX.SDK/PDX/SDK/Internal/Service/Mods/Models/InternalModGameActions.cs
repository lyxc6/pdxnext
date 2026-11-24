using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Models;

[Preserve]
internal class InternalModGameActions
{
	[Preserve]
	public bool install { get; set; }

	[Preserve]
	public bool enable { get; set; }

	[Preserve]
	public bool rate { get; set; }

	[Preserve]
	public bool publish { get; set; }

	[Preserve]
	public bool download { get; set; }

	[Preserve]
	public bool report { get; set; }

	[Preserve]
	public bool selectVersion { get; set; }

	[Preserve]
	public bool setActivationOrder { get; set; }

	[Preserve]
	public bool activationSets { get; set; }

	[Preserve]
	public bool view { get; set; }

	[Preserve]
	public bool modDependencies { get; set; }

	[Preserve]
	public bool multipleForumThreads { get; set; }

	[Preserve]
	public bool looseModFilesSupport { get; set; }
}
