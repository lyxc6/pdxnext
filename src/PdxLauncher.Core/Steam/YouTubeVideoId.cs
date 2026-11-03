namespace PdxLauncher.Core.Steam;

/// <summary>
/// 从各种 YouTube 链接里取出 11 位视频 ID。
/// <para>
/// Steam 的视频预览只接受 YouTube 视频 ID（<c>AddItemPreviewVideo</c>），不支持直传视频文件，
/// 所以这里只做「链接 → ID」的提取，不做任何下载。
/// </para>
/// </summary>
public static class YouTubeVideoId
{
    /// <summary>YouTube 视频 ID 长度。</summary>
    public const int Length = 11;

    /// <summary>从链接或裸 ID 里取视频 ID。</summary>
    /// <param name="input">用户输入，可能是完整链接、带参数的分享链接，或裸 ID。</param>
    /// <param name="videoId">解析成功时的 11 位 ID。</param>
    /// <returns>成功返回 true。</returns>
    public static bool TryParse(string? input, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();

        // 裸 ID：11 位合法字符。
        if (IsValidId(text))
        {
            videoId = text;
            return true;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            // 没有协议头的分享文本（如 youtu.be/xxx）也能救回来。
            if (!Uri.TryCreate("https://" + text, UriKind.Absolute, out uri))
            {
                return false;
            }
        }

        var host = uri.Host;
        if (!IsYouTubeHost(host))
        {
            return false;
        }

        // youtu.be/<id>
        if (host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var segment = uri.AbsolutePath.Trim('/');
            return Take(segment, out videoId);
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("v", out var v) && Take(v, out videoId))
        {
            return true;
        }

        // /embed/<id>、/shorts/<id>、/live/<id>、/v/<id>
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var kind = parts[0].ToLowerInvariant();
            if (kind is "embed" or "shorts" or "live" or "v" && Take(parts[1], out videoId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Take(string candidate, out string videoId)
    {
        videoId = string.Empty;
        if (!IsValidId(candidate))
        {
            return false;
        }

        videoId = candidate;
        return true;
    }

    private static bool IsValidId(string value)
    {
        if (value.Length != Length)
        {
            return false;
        }

        foreach (var ch in value)
        {
            var ok = ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsYouTubeHost(string host) =>
        host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = pair[..index];
            var value = index + 1 < pair.Length ? pair[(index + 1)..] : string.Empty;
            result[key] = Uri.UnescapeDataString(value);
        }

        return result;
    }
}
