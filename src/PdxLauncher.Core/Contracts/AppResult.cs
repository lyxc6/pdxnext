namespace PdxLauncher.Core.Contracts;

/// <summary>解析/操作结果，避免解析器把异常抛给 UI。</summary>
public sealed record AppResult<T>(bool IsSuccess, T? Value, string? ErrorMessage)
{
    /// <summary>成功。</summary>
    public static AppResult<T> Ok(T value) => new(true, value, null);

    /// <summary>失败。</summary>
    public static AppResult<T> Fail(string message) => new(false, default, message);
}
