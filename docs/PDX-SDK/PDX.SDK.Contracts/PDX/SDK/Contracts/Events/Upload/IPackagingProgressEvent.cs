using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Contracts.Events.Upload;

public interface IPackagingProgressEvent : IEventBase
{
	PackagingStage CurrentStage { get; }

	float Progress { get; }

	string Reference { get; }

	string SourceFolder { get; }
}
