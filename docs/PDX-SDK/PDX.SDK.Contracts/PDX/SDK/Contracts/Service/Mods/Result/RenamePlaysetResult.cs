using System.Runtime.Serialization;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class RenamePlaysetResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	[DataMember(Name = "id")]
	public int PlaysetId { get; set; }

	[Preserve]
	[DataMember(Name = "name")]
	public string Name { get; set; }
}
