namespace PdxLauncher.Core.Abstractions;

/// <summary>HTTP 抽象，便于离线自检注入 fake。</summary>
public interface IHttpFetcher
{
    Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken);

    Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken);
}
