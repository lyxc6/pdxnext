using PDX.SDK.Contracts;
using PDX.SDK.Internal.Platypatch.Models;

namespace PDX.SDK.Internal.Platypatch.Results;

internal class RepositoryMetaDataResult : Result
{
	public RepoMetaData RepositoryMetaData { get; set; }
}
