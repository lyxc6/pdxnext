using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Platypatch.Models;

public class Version
{
	[DataMember(Name = "completeSize")]
	public long CompleteSize { get; set; }

	[DataMember(Name = "exePath")]
	public string ExePath { get; set; }

	[DataMember(Name = "patches")]
	public Dictionary<string, Patch> Patches { get; set; }

	[DataMember(Name = "dependencies")]
	public Dependency[] Dependencies { get; set; }

	[DataMember(Name = "subRepositories")]
	public SubRepository[] Subrepositories { get; set; }
}
