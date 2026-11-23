using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Platypatch.Models;

public class RepoMetaData
{
	[DataMember(Name = "content")]
	public Content Content { get; set; }

	[DataMember(Name = "version")]
	public string Version { get; set; }
}
