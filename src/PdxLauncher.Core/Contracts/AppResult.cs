namespace PdxLauncher.Core.Contracts;

/// <summary>失败类别。展示层据此选文案，避免把中文写进 Core。</summary>
public enum ErrorKind
{
    /// <summary>未分类（历史调用点，等价于「说不清」）。</summary>
    Unknown,

    /// <summary>目标不存在（播放集 id 不在库里、数据库文件缺失等）。</summary>
    NotFound,

    /// <summary>与当前状态冲突（重名、删除活动播放集等）。</summary>
    Conflict,

    /// <summary>入参不合法（空名字、非法路径等），未触达存储。</summary>
    Invalid,

    /// <summary>读写失败（IO 异常、SQLite 异常、备份失败等）。</summary>
    Io,

    /// <summary>不支持的操作或格式。</summary>
    Unsupported,

    /// <summary>需要登录，或登录态已失效（凭据错误、token 过期、无权限）。</summary>
    Unauthorized,
}

/// <summary>
/// 解析/操作结果，避免解析器把异常抛给 UI。
/// <para><see cref="ErrorMessage"/> 只承载「错误码 + 参数」（如 <c>playSet-not-found:&lt;guid&gt;</c>），
/// 中文文案由展示层翻译；只有 <see cref="ErrorKind.Io"/> 的 detail 可能含原始异常文本，用于排障。</para>
/// </summary>
public sealed record AppResult<T>(bool IsSuccess, T? Value, string? ErrorMessage, ErrorKind Kind = ErrorKind.Unknown)
{
    /// <summary>成功。</summary>
    public static AppResult<T> Ok(T value) => new(true, value, null, ErrorKind.Unknown);

    /// <summary>失败，不带类别（保留给既有调用点）。</summary>
    public static AppResult<T> Fail(string message) => new(false, default, message, ErrorKind.Unknown);

    /// <summary>失败，带类别；<paramref name="detail"/> 是错误码，不是中文文案。</summary>
    public static AppResult<T> Fail(ErrorKind kind, string detail) => new(false, default, detail, kind);
}
