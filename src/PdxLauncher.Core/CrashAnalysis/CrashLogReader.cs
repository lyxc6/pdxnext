namespace PdxLauncher.Core.CrashAnalysis;

using System.Text;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 只读的日志/崩溃文件读取器：只打开共享读句柄，最多读文件尾部 N 字节；
/// 截断处落在半个 UTF-8 字符或半行上时丢掉残片，不会出现乱码或半截时间戳。
/// 任何失败都退化成空串，不向调用方抛异常。
/// </summary>
public static class CrashLogReader
{
    /// <summary>单份日志最多读取的字节数（崩溃包里的 debug.log 本机约 425 KB）。</summary>
    public const int DefaultMaxBytes = 2 * 1024 * 1024;

    /// <summary>读取文件尾部；文件不存在或读不到时返回空串。</summary>
    public static string ReadTail(IFileSystem fileSystem, string? path, int maxBytes = DefaultMaxBytes)
    {
        if (fileSystem is null || string.IsNullOrWhiteSpace(path) || maxBytes <= 0)
        {
            return string.Empty;
        }

        try
        {
            if (!fileSystem.FileExists(path))
            {
                return string.Empty;
            }

            using var stream = fileSystem.OpenRead(path);
            if (!stream.CanSeek)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }

            long length = stream.Length;
            if (length <= 0)
            {
                return string.Empty;
            }

            long start = Math.Max(0, length - maxBytes);
            int size = (int)Math.Min(length - start, maxBytes);
            stream.Seek(start, SeekOrigin.Begin);

            var buffer = new byte[size];
            int read = 0;
            while (read < size)
            {
                int chunk = stream.Read(buffer, read, size - read);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            int offset = 0;
            if (start > 0)
            {
                // 从文件中间开始时，首字节可能落在 UTF-8 续字节上。
                while (offset < read && (buffer[offset] & 0xC0) == 0x80)
                {
                    offset++;
                }
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            string text = encoding.GetString(buffer, offset, read - offset);

            if (start > 0)
            {
                // 丢掉截断处的前半行。
                int newline = text.IndexOf('\n');
                if (newline >= 0)
                {
                    text = text[(newline + 1)..];
                }
                else
                {
                    text = string.Empty;
                }
            }

            return text.TrimEnd('\uFFFD');
        }
        catch (Exception)
        {
            // 崩溃分析是只读诊断：读不到就留空，不能因此影响页面。
            return string.Empty;
        }
    }
}
