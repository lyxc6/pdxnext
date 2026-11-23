using System;
using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Service.Achievements.Models;

internal class CachedAchievementImageData
{
	private byte[] _binaryData;

	public string Id { get; internal set; }

	public bool Achieved { get; internal set; }

	public string Base64Data { get; internal set; }

	[IgnoreDataMember]
	public byte[] BinaryData => _binaryData ?? (_binaryData = Convert.FromBase64String(Base64Data ?? ""));
}
