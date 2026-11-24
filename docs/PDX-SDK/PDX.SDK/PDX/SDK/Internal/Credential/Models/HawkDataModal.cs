using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Credential.Models;

public class HawkDataModal
{
	public string email;

	[DataMember(Name = "sha256(password+salt)")]
	public string hash;
}
