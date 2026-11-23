using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Platypatch.Models;

public class SubRepository
{
	[DataMember(Name = "repositoryName")]
	public string RepositoryName { get; set; }

	[DataMember(Name = "compatibleVersions")]
	public string[] CompatibleVersions { get; set; }

	[DataMember(Name = "relativePath")]
	public string RelativePath { get; set; }
}
