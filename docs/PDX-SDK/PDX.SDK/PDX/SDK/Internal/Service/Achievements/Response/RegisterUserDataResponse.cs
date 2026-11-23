using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Achievements.Response;

[Preserve]
internal class RegisterUserDataResponse : HTTPBodyBase
{
	[Preserve]
	public Dictionary<string, object> data { get; set; }
}
