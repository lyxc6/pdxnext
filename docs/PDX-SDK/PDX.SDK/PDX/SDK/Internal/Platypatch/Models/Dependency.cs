using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Platypatch.Models;

public class Dependency
{
	[DataMember(Name = "exe")]
	public DependencyExe Exe { get; set; }
}
