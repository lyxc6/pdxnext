namespace PdxLauncher.Core.Infrastructure;

using System.Globalization;
using System.Text;

/// <summary>
/// 追加式文件日志。所有层共用同一个实例，保证日志只有一条写入路径。
/// 目录不可写时自动退化为只写调试输出，绝不因为日志失败而抛异常。
/// <para>
/// 单文件上限 5MB，超过后轮转为 <c>.1</c> / <c>.2</c> / <c>.3</c>（最多留三份），
/// 避免长期运行后日志无限增长。写入走缓冲，每 2 秒或遇到 ERROR 时落盘，
/// 崩溃最多丢最近两秒——换掉的是每条日志一次刷盘的开销。
/// </para>
/// </summary>
public sealed class FileLogger : Abstractions.IAppLogger, IDisposable
{
    private const long MaxBytes = 5L * 1024 * 1024;
    private const int MaxArchives = 3;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly string? _path;
    private StreamWriter? _writer;
    private long _writtenBytes;
    private DateTime _lastFlushUtc = DateTime.UtcNow;

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

            // 上次退出时已经写满：先轮转再开新文件，否则一启动就超限。
            if (TryGetLength(path) > MaxBytes)
            {
                RotateFiles(path);
            }

            OpenWriter(path);
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
            if (_writer is not null)
            {
                try
                {
                    _writer.WriteLine(line);
                    _writtenBytes += Utf8NoBom.GetByteCount(line) + Environment.NewLine.Length;

                    if (level == "ERROR" || DateTime.UtcNow - _lastFlushUtc >= FlushInterval)
                    {
                        FlushLocked();
                    }

                    if (_writtenBytes > MaxBytes)
                    {
                        RotateLocked();
                    }
                }
                catch (IOException)
                {
                    // 磁盘满 / 文件被占用：放弃这一行，别把异常抛给业务。
                }
            }
        }

        System.Diagnostics.Debug.WriteLine(line);
    }

    private void OpenWriter(string path)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writtenBytes = stream.Length;
        _writer = new StreamWriter(stream, Utf8NoBom)
        {
            AutoFlush = false,
        };
        _lastFlushUtc = DateTime.UtcNow;
    }

    private void FlushLocked()
    {
        try
        {
            _writer?.Flush();
        }
        catch (IOException)
        {
            // 刷盘失败不能影响业务；下一次写入会再试。
        }

        _lastFlushUtc = DateTime.UtcNow;
    }

    private void RotateLocked()
    {
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }

        try
        {
            _writer?.Dispose();
            _writer = null;
            RotateFiles(_path);
            OpenWriter(_path);
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

    /// <summary>把 <c>path</c> 挪成 <c>path.1</c>，旧的依次后移，最老的一份丢弃。</summary>
    private static void RotateFiles(string path)
    {
        for (int index = MaxArchives; index > 1; index--)
        {
            string from = path + "." + (index - 1).ToString(CultureInfo.InvariantCulture);
            string to = path + "." + index.ToString(CultureInfo.InvariantCulture);
            if (File.Exists(from))
            {
                File.Move(from, to, overwrite: true);
            }
        }

        if (File.Exists(path))
        {
            File.Move(path, path + ".1", overwrite: true);
        }
    }

    private static long TryGetLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                return;
            }

            try
            {
                _writer.Flush();
            }
            catch (IOException)
            {
                // 退出路径上同样不抛。
            }

            _writer.Dispose();
            _writer = null;
        }
    }
}
