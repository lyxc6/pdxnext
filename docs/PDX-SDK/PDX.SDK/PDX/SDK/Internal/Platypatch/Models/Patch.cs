using System;
using System.Runtime.Serialization;

namespace PDX.SDK.Internal.Platypatch.Models;

public class Patch
{
	[DataMember(Name = "timestamp")]
	public DateTime TimeStamp { get; set; }

	[DataMember(Name = "sizeUnpacked")]
	public long SizeUnpacked { get; set; }

	[DataMember(Name = "sizePacked")]
	public long SizePacked { get; set; }
}
