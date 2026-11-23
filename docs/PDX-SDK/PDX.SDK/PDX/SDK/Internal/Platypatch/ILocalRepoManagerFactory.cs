using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal interface ILocalRepoManagerFactory
{
	ILocalRepoManager Create(string absoluteRepoPath, string appName, string version);
}
