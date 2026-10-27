using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>Paradox 元数据 GraphQL 客户端。</summary>
public interface IMetadataClient
{
    Task<IReadOnlyList<GameMetadataDto>> GetMetadataAsync(
        IReadOnlyList<string> gameIds,
        CancellationToken cancellationToken);
}
