namespace PdxLauncher.Core.Playsets;

/// <summary>sqlite 原生调用失败时抛出的异常，携带 sqlite 结果码便于调用方区分"表不存在"等情况。</summary>
public sealed class SqliteException : Exception
{
    /// <summary>创建异常。</summary>
    public SqliteException(int resultCode, string message)
        : base(message)
    {
        ResultCode = resultCode;
    }

    /// <summary>创建带内部异常的异常。</summary>
    public SqliteException(int resultCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ResultCode = resultCode;
    }

    /// <summary>sqlite3_errcode 返回的结果码（0 = OK）。</summary>
    public int ResultCode { get; }
}
