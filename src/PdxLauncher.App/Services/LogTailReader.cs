namespace PdxLauncher.App.Services;

using System.Globalization;
using System.Text;

using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 日志尾部读取与统计。读文件只用 <see cref="FileShare.ReadWrite"/> + <see cref="FileShare.Delete"/> 的只读句柄，
/// 不阻塞 <see cref="FileLogger"/> 继续追加，也不删除、不改动任何日志。
/// 任何失败（文件不存在、被独占、权限不足）都退化为空串 / 0，不向 UI 抛异常。
/// </summary>
public static class LogTailReader
{
    /// <summary>默认读取的最后行数。</summary>
    public const int DefaultMaxLines = 200;

    /// <summary>默认最多读取的字节数（只读尾部，避免大文件把整页拖慢）。</summary>
    public const int DefaultMaxBytes = 128 * 1024;

    /// <summary>
    /// 读取日志文件末尾 <paramref name="maxLines"/> 行。只读最后 <paramref name="maxBytes"/> 字节；
    /// 截断处落在半个 UTF-8 字符或半行上时丢弃该残片，不会出现乱码行。
    /// </summary>
    public static string ReadTail(string? path, int maxLines = DefaultMaxLines, int maxBytes = DefaultMaxBytes)
    {
        if (string.IsNullOrWhiteSpace(path) || maxLines <= 0 || maxBytes <= 0)
        {
            return string.Empty;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

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
                // 从文件中间开始时，首字节可能落在某个 UTF-8 字符的续字节上，跳过直到字符起点。
                while (offset < read && (buffer[offset] & 0xC0) == 0x80)
                {
                    offset++;
                }
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            string text = encoding.GetString(buffer, offset, read - offset).TrimEnd('\uFFFD');

            string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            int first = 0;
            if (start > 0 && lines.Length > 0)
            {
                // 截断处的前半行不要：它要么缺时间戳，要么缺正文。
                first = 1;
            }

            int last = lines.Length;
            if (last > first && lines[last - 1].Length == 0)
            {
                // 文件以换行结尾时，Split 会多出一个空段。
                last--;
            }

            if (last <= first)
            {
                return string.Empty;
            }

            int skip = Math.Max(first, last - maxLines);
            return string.Join("\n", lines, skip, last - skip);
        }
        catch (Exception)
        {
            // 诊断页显示空尾部即可，不能因为读日志失败而影响页面本身。
            return string.Empty;
        }
    }

    /// <summary>
    /// 按级别过滤日志文本。<see cref="LogLevelFilter.All"/> 原样返回；
    /// 其余按「以时间戳开头的行」切块，异常的堆栈续行跟随所属块一起保留。
    /// </summary>
    public static string Filter(string? text, LogLevelFilter filter)
    {
        if (string.IsNullOrEmpty(text) || filter == LogLevelFilter.All)
        {
            return text ?? string.Empty;
        }

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new StringBuilder(text.Length);
        bool inBlock = false;
        bool keep = false;

        foreach (string line in lines)
        {
            if (IsHeader(line))
            {
                inBlock = true;
                keep = filter == LogLevelFilter.Error
                    ? line.Contains("[ERROR]", StringComparison.Ordinal)
                    : line.Contains("[WARN]", StringComparison.Ordinal)
                        || line.Contains("[ERROR]", StringComparison.Ordinal);
            }

            if (!inBlock || !keep)
            {
                continue;
            }

            output.Append(line).Append('\n');
        }

        return output.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// 与 <see cref="Filter"/> 相同的过滤规则，但把每个日志块（含异常堆栈）整体倒序，
    /// 让最新一条排在最前面——性能页的只读日志框高度有限，从上往下读就该从最新读起。
    /// </summary>
    public static string FilterNewestFirst(string? text, LogLevelFilter filter)
    {
        string filtered = Filter(text, filter);
        if (string.IsNullOrEmpty(filtered))
        {
            return string.Empty;
        }

        var blocks = new List<string>();
        var current = new StringBuilder();
        foreach (string line in filtered.Split('\n'))
        {
            if (IsHeader(line) && current.Length > 0)
            {
                blocks.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(line);
        }

        if (current.Length > 0)
        {
            blocks.Add(current.ToString());
        }

        if (blocks.Count <= 1)
        {
            return filtered;
        }

        blocks.Reverse();
        return string.Join("\n", blocks);
    }

    /// <summary>
    /// 统计日志占用：当前文件、<c>.1</c> ~ <c>.3</c> 归档的数量与合计字节，以及主文件最后写入时间。
    /// 单个文件读不到就跳过，整体失败返回 <see cref="LogMetrics.Empty"/>。
    /// </summary>
    public static LogMetrics Measure(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return LogMetrics.Empty;
        }

        long currentBytes = 0;
        long totalBytes = 0;
        int archiveCount = 0;
        DateTimeOffset? lastWrite = null;

        for (int index = 0; index <= FileLogger.MaxArchives; index++)
        {
            string candidate = index == 0
                ? logPath
                : logPath + "." + index.ToString(CultureInfo.InvariantCulture);

            try
            {
                var info = new FileInfo(candidate);
                if (!info.Exists)
                {
                    continue;
                }

                totalBytes += info.Length;
                if (index == 0)
                {
                    currentBytes = info.Length;
                    lastWrite = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                }
                else
                {
                    archiveCount++;
                }
            }
            catch (Exception)
            {
                // 被写入方占用或权限不足：跳过这一份，其余照常统计。
            }
        }

        return new LogMetrics(currentBytes, archiveCount, totalBytes, lastWrite);
    }

    private static bool IsHeader(string line)
    {
        // 正常行：2026-11-04 15:20:31.123 [INFO] ...（时间戳 19 字符 + 空格）。
        if (line.Length < 20)
        {
            return false;
        }

        return IsDigitPair(line, 0) && line[4] == '-' && IsDigitPair(line, 5) && line[7] == '-'
            && IsDigitPair(line, 8) && line[10] == ' ' && IsDigitPair(line, 11) && line[13] == ':'
            && IsDigitPair(line, 14) && line[16] == ':' && IsDigitPair(line, 17);
    }

    private static bool IsDigitPair(string line, int index) =>
        char.IsAsciiDigit(line[index]) && char.IsAsciiDigit(line[index + 1]);
}

/// <summary>日志级别过滤。</summary>
public enum LogLevelFilter
{
    /// <summary>全部行。</summary>
    All,

    /// <summary>WARN 与 ERROR。</summary>
    Warning,

    /// <summary>只有 ERROR。</summary>
    Error,
}

/// <summary>日志文件统计。<paramref name="LastWriteUtc"/> 取主文件的最后写入时间，读不到时为 null。</summary>
public sealed record LogMetrics(long CurrentBytes, int ArchiveCount, long TotalBytes, DateTimeOffset? LastWriteUtc)
{
    /// <summary>全 0 的空统计，用于文件缺失或不可读。</summary>
    public static LogMetrics Empty { get; } = new(0, 0, 0, null);
}
