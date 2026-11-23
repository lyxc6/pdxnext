using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Achievements.Request;

[Preserve]
internal class RegisterUserDataRequest
{
	[Preserve]
	public string id;

	[Preserve]
	public bool isAchieved;

	[Preserve]
	public int progress;
}
