namespace PdxLauncher.Core.Abstractions;

/// <summary>HTTP 抽象，便于离线自检注入 fake。</summary>
public interface IHttpFetcher
{
    Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken);

    Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken);

    /// <summary>
    /// POST <c>application/x-www-form-urlencoded</c>。Steam 的 keyless 接口只吃表单
    /// （GetPublishedFileDetails 用 JSON body 会 400）。非 2xx 抛 <see cref="HttpRequestException"/>。
    /// </summary>
    Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken);
}
