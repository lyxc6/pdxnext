using System;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Contracts.Events.Upload;

namespace PDX.SDK.Internal.Events.Upload;

public class PackagingProgressEvent : IPackagingProgressEvent, IEventBase
{
	public Guid EventId { get; set; }

	public PackagingStage CurrentStage { get; set; }

	public float Progress { get; set; }

	public string SourceFolder { get; set; }

	public string Reference { get; set; }
}
