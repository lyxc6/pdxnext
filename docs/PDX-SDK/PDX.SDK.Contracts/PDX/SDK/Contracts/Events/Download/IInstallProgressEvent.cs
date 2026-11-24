using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Contracts.Events.Download;

public interface IInstallProgressEvent : IEventBase
{
	InstallStage CurrentStage { get; }

	float Progress { get; }

	string Reference { get; }

	string RepositoryName { get; }

	string AppName { get; }

	string InstallFolder { get; }

	string InstallingVersion { get; }
}
