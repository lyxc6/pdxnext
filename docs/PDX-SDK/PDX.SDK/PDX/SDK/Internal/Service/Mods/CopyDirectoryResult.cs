using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class CopyDirectoryResult : Result
{
	[Preserve]
	public string TargetPath { get; set; }

	[Preserve]
	public bool WasTargetAlreadyPresent { get; set; }
}
