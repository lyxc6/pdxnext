using System.Threading.Tasks;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal interface IRemoteRepositoryFactory
{
	Task<IRemoteRepository> Create(string repositoryName);
}
