namespace PdxLauncher.App.Services;

using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// Windows 事件日志只读读取器：通过 Windows 自带的 Event Logging API（P/Invoke）读取，
/// 不依赖任何 NuGet 包。只取崩溃时间前后一个短窗口内的取证事件，读不到一律返回空。
/// </summary>
public sealed partial class WindowsEventLogReader : IWindowsEventLogReader
{
    private const int MaxMessageChars = 400;
    private static readonly TimeSpan WindowCap = TimeSpan.FromMinutes(10);
    private const int EventlogSequentialRead = 0x0004;

    /// <inheritdoc />
    public IReadOnlyList<WindowsEventEvidence> ReadNear(
        DateTimeOffset crashTime,
        int windowMinutes,
        int maxEvents,
        CancellationToken cancellationToken = default)
    {
        try
        {
            int window = Math.Clamp(windowMinutes, 1, (int)WindowCap.TotalMinutes);
            int limit = Math.Clamp(maxEvents, 1, 100);
            var start = crashTime.AddMinutes(-window).LocalDateTime;
            var end = crashTime.AddMinutes(window).LocalDateTime;
            var result = new List<WindowsEventEvidence>();

            Collect("Application", start, end, limit, result, ClassifyApplication, cancellationToken);
            Collect("System", start, end, limit - result.Count, result, ClassifySystem, cancellationToken);
            return result.Take(limit).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 读不到事件日志是常态（权限/服务未启动/非 Windows），绝不让崩溃页打不开。
            return Array.Empty<WindowsEventEvidence>();
        }
    }

    private static void Collect(
        string logName,
        DateTime start,
        DateTime end,
        int limit,
        List<WindowsEventEvidence> result,
        Func<WinEventRecord, WindowsEventEvidence?> classify,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenEventLog(null, logName);
            if (handle == IntPtr.Zero)
            {
                return;
            }

            const int bufferSize = 64 * 1024;
            var buffer = new byte[bufferSize];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Count >= limit)
                {
                    return;
                }

                if (!ReadEventLog(handle, EventlogSequentialRead, 0, buffer, (uint)buffer.Length, out uint read, out uint needed))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 122 /* ERROR_INSUFFICIENT_BUFFER */ && needed > bufferSize)
                    {
                        buffer = new byte[needed];
                        continue;
                    }

                    // ERROR_HANDLE_EOF / 其它读取失败：当前日志已读完，正常返回。
                    return;
                }

                if (read == 0)
                {
                    return;
                }

                int offset = 0;
                while (offset + 12 <= read)
                {
                    uint length = BitConverter.ToUInt32(buffer, offset);
                    if (length < 12 || offset + length > read)
                    {
                        break;
                    }

                    var record = ParseRecord(buffer, offset, (int)length);
                    offset += (int)length;
                    if (record is not { } parsed || parsed.TimeGenerated < start || parsed.TimeGenerated > end)
                    {
                        continue;
                    }

                    var evidence = classify(parsed);
                    if (evidence is not null)
                    {
                        result.Add(evidence);
                        if (result.Count >= limit)
                        {
                            return;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 单个日志失败不影响分析。
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseEventLog(handle);
            }
        }
    }

    private static WinEventRecord? ParseRecord(byte[] buffer, int offset, int length)
    {
        try
        {
            // EVENTLOGRECORD：Length(0) Reserved(4) RecordNumber(8) TimeGenerated(12) TimeWritten(16)
            // EventID(20,16位) EventType(22) NumStrings(24,16位) EventCategory(26) UserOffset(28)
            // UserLength(30) SourceOffset(32) ComputerOffset(36) 字符串区从 0x38(56) 开始。
            uint timeGenerated = BitConverter.ToUInt32(buffer, offset + 12);
            ushort eventId = BitConverter.ToUInt16(buffer, offset + 20);
            ushort numStrings = BitConverter.ToUInt16(buffer, offset + 24);
            short userOffset = BitConverter.ToInt16(buffer, offset + 28);
            short sourceOffset = BitConverter.ToInt16(buffer, offset + 32);
            var time = DateTime.FromFileTimeUtc(timeGenerated).ToLocalTime();

            string source = ReadAnsiZAt(buffer, offset + sourceOffset, offset + length);

            // 事件消息是格式化模板，真正的数据（故障模块名等）在 USER_DATA 的插入字符串里。
            var insertions = new List<string>();
            int userStart = offset + userOffset;
            int userEnd = offset + length;
            if (userOffset > 0 && userStart >= offset && userStart < userEnd)
            {
                string unicode = Encoding.Unicode.GetString(buffer, userStart, userEnd - userStart);
                int nullIndex = unicode.IndexOf('\0');
                if (nullIndex >= 0)
                {
                    unicode = unicode[..nullIndex];
                }

                if (PrintableRatio(unicode) > 0.5)
                {
                    insertions.AddRange(unicode.Split('\0', StringSplitOptions.RemoveEmptyEntries));
                }
            }

            // 回退：部分事件把插入串也放在固定字符串区，取前几个可读的。
            if (insertions.Count == 0 && numStrings > 0)
            {
                int strings = offset + 56;
                for (int i = 0; i < numStrings && strings < offset + length; i++)
                {
                    insertions.Add(ReadAnsiZ(buffer, ref strings, offset + length));
                }
            }

            string message = string.Join(" | ", insertions);
            return new WinEventRecord(eventId, source, message, time);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ReadAnsiZAt(byte[] buffer, int start, int end)
    {
        int position = Math.Clamp(start, 0, end);
        return ReadAnsiZ(buffer, ref position, end);
    }

    private static string ReadAnsiZ(byte[] buffer, ref int position, int end)
    {
        int start = position;
        while (position < end && buffer[position] != 0)
        {
            position++;
        }

        string text = Encoding.Default.GetString(buffer, start, position - start).Trim();
        position++; // 跳过结尾 0
        return text;
    }

    private static double PrintableRatio(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        int printable = 0;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                printable++;
            }
        }

        return (double)printable / text.Length;
    }

    private static WindowsEventEvidence? ClassifyApplication(WinEventRecord record)
    {
        if (record.EventId != 1000)
        {
            return null;
        }

        var match = ModulePattern().Match(record.Message);
        string module = match.Success ? match.Groups["module"].Value.Trim() : string.Empty;
        return new WindowsEventEvidence(
            WindowsEventKind.ApplicationFaultModule,
            record.EventId,
            record.Source,
            new DateTimeOffset(record.TimeGenerated),
            module.Length > 0 ? module : null,
            Truncate(record.Message));
    }

    private static WindowsEventEvidence? ClassifySystem(WinEventRecord record)
    {
        WindowsEventKind? kind = record.EventId switch
        {
            2004 => WindowsEventKind.LowVirtualMemory,
            4101 or 13 or 14 or 153 when IsDisplaySource(record.Source) => WindowsEventKind.GraphicsDriverReset,
            _ => null,
        };

        if (kind is null)
        {
            if (WheaPattern().IsMatch(record.Source))
            {
                kind = WindowsEventKind.HardwareError;
            }
            else if (DiskSourcePattern().IsMatch(record.Source))
            {
                kind = WindowsEventKind.DiskError;
            }
        }

        return kind is null
            ? null
            : new WindowsEventEvidence(
                kind.Value,
                record.EventId,
                record.Source,
                new DateTimeOffset(record.TimeGenerated),
                null,
                Truncate(record.Message));
    }

    private static bool IsDisplaySource(string source) =>
        source.Contains("Display", StringComparison.OrdinalIgnoreCase)
        || GraphicsDriverSourcePattern().IsMatch(source);

    private static string Truncate(string text) =>
        string.IsNullOrEmpty(text) ? string.Empty
            : text.Length <= MaxMessageChars ? text
            : text[..MaxMessageChars] + "…";

    private readonly record struct WinEventRecord(
        long EventId,
        string Source,
        string Message,
        DateTime TimeGenerated);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenEventLog(string? lpUNCServerName, string lpSourceName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadEventLog(
        IntPtr hEventLog,
        uint dwReadFlags,
        uint dwRecordOffset,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint pnBytesRead,
        out uint pnMinNumberOfBytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseEventLog(IntPtr hEventLog);

    [GeneratedRegex(@"Faulting module name:\s*(?<module>[^,\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModulePattern();

    [GeneratedRegex(@"WHEA", RegexOptions.IgnoreCase)]
    private static partial Regex WheaPattern();

    [GeneratedRegex(@"nvlddmkm|amdkmdag|amdwddmg|igfx|Display", RegexOptions.IgnoreCase)]
    private static partial Regex GraphicsDriverSourcePattern();

    [GeneratedRegex(@"^(disk|ntfs|stornvme|storahci)$", RegexOptions.IgnoreCase)]
    private static partial Regex DiskSourcePattern();
}
