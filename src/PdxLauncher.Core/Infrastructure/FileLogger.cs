namespace PdxLauncher.Core.Infrastructure;

using System.Globalization;
using System.Text;

/// <summary>
/// 追加式文件日志。所有层共用同一个实例，保证日志只有一条写入路径。
/// 目录不可写时自动退化为只写调试输出，绝不因为日志失败而抛异常。
/// </summary>
public sealed class FileLogger : Abstractions.IAppLogger, IDisposable
{
    private readonly object _gate = new();
    private readonly string? _path;
    private readonly StreamWriter? _writer;

    public FileLogger(string? path)
    {
        _path = path;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }
        catch (IOException)
        {
            _writer = null;
        }
        catch (UnauthorizedAccessException)
        {
            _writer = null;
        }
    }

    public string? LogPath => _path;

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");

        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_gate)
        {
            _writer?.WriteLine(line);
        }

        System.Diagnostics.Debug.WriteLine(line);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
        }
    }
}
