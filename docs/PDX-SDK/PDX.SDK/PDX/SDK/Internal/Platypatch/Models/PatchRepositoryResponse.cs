using System.Runtime.Serialization;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Platypatch.Models;

public class PatchRepositoryResponse : HTTPBodyBase
{
	[DataMember(Name = "ttl")]
	public string Ttl { get; set; }

	[DataMember(Name = "prepend")]
	public string Prepend { get; set; }

	[DataMember(Name = "append")]
	public string Append { get; set; }
}
