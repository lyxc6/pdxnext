namespace PdxLauncher.Core.Infrastructure;

using System.Globalization;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 崩溃标记文件的唯一写入点：把一次未处理异常的完整信息落成
/// <c>&lt;logs&gt;\crash-yyyyMMdd-HHmmss.txt</c>，供事后定位。
/// <para>
/// 这里刻意不做任何「聪明」的降级：写失败就返回 <see cref="AppResult{T}"/> 失败，
/// 由调用方决定是否还要弹提示。绝不因为写崩溃记录而再抛一次异常。
/// </para>
/// </summary>
public sealed class CrashReportWriter
{
    /// <summary>崩溃记录文件名前缀。</summary>
    public const string FileNamePrefix = "crash-";

    /// <summary>崩溃记录文件扩展名。</summary>
    public const string FileExtension = ".txt";

    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly string _directory;

    public CrashReportWriter(IFileSystem fileSystem, IClock clock, string directory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _fileSystem = fileSystem;
        _clock = clock;
        _directory = directory;
    }

    /// <summary>崩溃记录写入目录。</summary>
    public string OutputDirectory => _directory;

    /// <summary>
    /// 写一份崩溃记录。成功返回落盘路径；失败返回 <see cref="ErrorKind.Io"/>。
    /// </summary>
    /// <param name="message">人类可读的一句话说明（例如「未处理的异常」）。</param>
    /// <param name="exception">异常本体；为 null 时只记录 message。</param>
    /// <param name="context">可选上下文，例如当前游戏 id、页面名。</param>
    public AppResult<string> Write(string message, Exception? exception, string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        try
        {
            _fileSystem.EnsureDirectory(_directory);
            var path = NextPath();
            _fileSystem.WriteAllTextAtomic(path, Compose(message, exception, context));
            return AppResult<string>.Ok(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return AppResult<string>.Fail(ErrorKind.Io, "crash-write:" + ex.Message);
        }
    }

    /// <summary>
    /// 把异常摊成可复制的纯文本：时间、说明、上下文、运行时、异常链、堆栈。
    /// 顺序固定，方便用 <c>crash-*.txt</c> 做 diff。
    /// </summary>
    public static string Compose(string message, Exception? exception, string? context)
    {
        var builder = new System.Text.StringBuilder(1024);
        builder.Append("time: ").Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("message: ").Append(message).Append('\n');

        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.Append("context: ").Append(context).Append('\n');
        }

        builder.Append("runtime: ").Append(Environment.Version.ToString()).Append('\n');
        builder.Append("os: ").Append(Environment.OSVersion.ToString()).Append('\n');
        builder.Append("arch: ").Append(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()).Append('\n');

        if (exception is not null)
        {
            builder.Append('\n').Append("--- exception ---").Append('\n');
            AppendException(builder, exception, 0);
        }

        return builder.ToString();
    }

    private static void AppendException(System.Text.StringBuilder builder, Exception exception, int depth)
    {
        var indent = new string(' ', depth * 2);
        builder.Append(indent).Append("type: ").Append(exception.GetType().FullName).Append('\n');
        builder.Append(indent).Append("hresult: 0x").Append(exception.HResult.ToString("X8", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(indent).Append("message: ").Append(exception.Message).Append('\n');

        if (!string.IsNullOrEmpty(exception.Source))
        {
            builder.Append(indent).Append("source: ").Append(exception.Source).Append('\n');
        }

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            builder.Append(indent).Append("stack:").Append('\n').Append(exception.StackTrace).Append('\n');
        }

        if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                builder.Append('\n').Append(indent).Append("--- inner ---").Append('\n');
                AppendException(builder, inner, depth + 1);
            }

            return;
        }

        if (exception.InnerException is not null)
        {
            builder.Append('\n').Append(indent).Append("--- inner ---").Append('\n');
            AppendException(builder, exception.InnerException, depth + 1);
        }
    }

    /// <summary>
    /// 同秒内连续崩溃时不能互相覆盖：文件名带毫秒，仍然撞车就追加序号。
    /// </summary>
    private string NextPath()
    {
        var stamp = _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(_directory, FileNamePrefix + stamp + FileExtension);
        var index = 1;
        while (_fileSystem.FileExists(path))
        {
            path = Path.Combine(
                _directory,
                FileNamePrefix + stamp + "-" + index.ToString(CultureInfo.InvariantCulture) + FileExtension);
            index++;
        }

        return path;
    }
}
