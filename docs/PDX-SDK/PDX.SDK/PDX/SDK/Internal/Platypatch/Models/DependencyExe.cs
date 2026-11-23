using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Platypatch.Models;

public class DependencyExe
{
	[DataMember(Name = "path")]
	public string Path { get; set; }

	[DataMember(Name = "exePath")]
	public string ExePath { get; set; }

	[DataMember(Name = "acceptedCodes")]
	public int[] AcceptedCodes { get; set; }

	[DataMember(Name = "dependencySize")]
	public long DependencySize { get; set; }

	[DataMember(Name = "parameters")]
	public string[] Parameters { get; set; }
}
