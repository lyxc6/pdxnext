using System;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Contracts.Events.Download;

namespace PDX.SDK.Internal.Events.Download;

public class InstallProgressEvent : IInstallProgressEvent, IEventBase
{
	public Guid EventId { get; set; }

	public InstallStage CurrentStage { get; set; }

	public float Progress { get; set; }

	public string RepositoryName { get; set; }

	public string AppName { get; set; }

	public string InstallFolder { get; set; }

	public string InstallingVersion { get; set; }

	public string Reference { get; set; }
}
