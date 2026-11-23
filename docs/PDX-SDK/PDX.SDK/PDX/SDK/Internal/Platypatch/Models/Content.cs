using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Platypatch.Models;

public class Content
{
	[DataMember(Name = "lastUpdated")]
	public string LastUpdated { get; set; }

	[DataMember(Name = "versionOrder")]
	public string[] VersionOrder { get; set; }

	[DataMember(Name = "versions")]
	public Dictionary<string, Version> Versions { get; set; }
}
