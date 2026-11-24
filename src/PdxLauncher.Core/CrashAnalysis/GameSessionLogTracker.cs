namespace PdxLauncher.Core.CrashAnalysis;

using System.Globalization;
using System.Text;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 本次游戏会话的只读错误采集器：游戏运行时按字节偏移增量读取日志，
/// 只统计会话开始之后写入的错误块，跨日志去重并按签名聚类；游戏结束后固化快照。
/// <para>
/// 不写任何文件、不联网、不启动进程。所有读取失败都吞掉并标记 Degraded，不抛到 UI。
/// CS2 的 Unity/Coherent 日志与 P 社老游戏的 logs 清单共用同一套会话统计口径。
/// </para>
/// </summary>
public sealed class GameSessionLogTracker
{
    private const int MaxReadBytesPerPoll = 2 * 1024 * 1024;

    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;

    private GameInstallation? _game;
    private IReadOnlyList<SessionLogSource> _sources = Array.Empty<SessionLogSource>();
    private int? _pid;
    private DateTimeOffset _startTime;
    private bool _started;
    private bool _degraded;
    private readonly Dictionary<string, LogCursor> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupAccumulator> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenErrors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenFatals = new(StringComparer.Ordinal);
    private SessionFatalInfo? _fatalInfo;
    private int _errorBlocks;

    /// <summary>仅用文件系统的构造（时间取默认时钟）。</summary>
    public GameSessionLogTracker(IFileSystem fileSystem, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);
        _fileSystem = fileSystem;
        _clock = clock;
    }

    /// <summary>当前是否有正在进行的会话。</summary>
    public bool IsRunning => _started;

    /// <summary>开始一个新会话，重置所有累计器与读取游标。</summary>
    public void Begin(GameInstallation game, int? processId, DateTimeOffset? startTime = null)
    {
        ArgumentNullException.ThrowIfNull(game);

        _game = game;
        _pid = processId;
        _startTime = startTime ?? _clock.UtcNow.ToLocalTime();
        _sources = SessionLogCatalog.Resolve(_fileSystem, game);

        _cursors.Clear();
        _groups.Clear();
        _seenErrors.Clear();
        _seenFatals.Clear();
        _fatalInfo = null;
        _errorBlocks = 0;
        _degraded = false;
        _started = true;

        // 从当前文件末尾开始，避免把上次会话遗留的日志算进来。
        foreach (var source in _sources)
        {
            if (!_fileSystem.FileExists(source.Path))
            {
                continue;
            }

            try
            {
                _cursors[source.Name] = new LogCursor(_fileSystem.GetFileSize(source.Path));
            }
            catch (Exception)
            {
                _degraded = true;
            }
        }
    }

    /// <summary>增量读取一次；应在游戏运行期间定期调用。</summary>
    public void Poll()
    {
        if (!_started)
        {
            return;
        }

        foreach (var source in _sources)
        {
            if (!_fileSystem.FileExists(source.Path))
            {
                continue;
            }

            try
            {
                if (!_cursors.TryGetValue(source.Name, out var cursor))
                {
                    // 会话期间新建的日志：从 0 开始读，但只统计会话时间之后的块。
                    cursor = new LogCursor(0);
                    _cursors[source.Name] = cursor;
                }

                long length = _fileSystem.GetFileSize(source.Path);
                if (length < cursor.Offset)
                {
                    // 日志被轮转/截断：从头再来。
                    cursor.Offset = 0;
                    cursor.Pending = string.Empty;
                }

                long available = length - cursor.Offset;
                if (available <= 0)
                {
                    continue;
                }

                if (available > MaxReadBytesPerPoll)
                {
                    // 日志暴涨时优先追到尾部，不阻塞 UI；这段积压标记为读取降级。
                    cursor.Offset = length - MaxReadBytesPerPoll;
                    cursor.Pending = string.Empty;
                    _degraded = true;
                }

                string text = ReadRange(source.Path, cursor.Offset, length, out long nextOffset);
                cursor.Offset = nextOffset;
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                // 把上次残留的半行接到本次开头，再切出新的残留。
                string combined = cursor.Pending + text;
                int lastNewline = combined.LastIndexOf('\n');
                string complete = lastNewline >= 0 ? combined[..(lastNewline + 1)] : string.Empty;
                cursor.Pending = lastNewline >= 0 ? combined[(lastNewline + 1)..] : combined;

                if (complete.Length == 0)
                {
                    continue;
                }

                var blocks = CrashLogParser.ParseLogBlocks(complete, _startTime);
                foreach (var block in blocks)
                {
                    RecordFatal(block, source.Name);
                    if (!CrashLogParser.IsErrorBlock(block))
                    {
                        continue;
                    }

                    // 只统计会话开始之后写入的块（时间戳已锚定到会话日期）。
                    if (block.Time is { } time && time < _startTime.AddSeconds(-2))
                    {
                        continue;
                    }

                    string key = block.Time?.UtcTicks.ToString(CultureInfo.InvariantCulture) + "|"
                        + block.Origin + "|" + CrashLogParser.Signature(block);
                    if (!_seenErrors.Add(key))
                    {
                        continue; // 同一错误在 error/game/debug 里重复出现只算一次。
                    }

                    _errorBlocks++;
                    string signature = CrashLogParser.Signature(block);
                    string? sourcePath = CrashLogParser.ExtractSourcePath(block.Text);
                    if (!_groups.TryGetValue(signature, out var group))
                    {
                        group = new GroupAccumulator(signature, sourcePath, Truncate(block.Message, 220));
                        _groups[signature] = group;
                    }
                    else
                    {
                        group.Count++;
                        group.Source ??= sourcePath;
                    }
                }
            }
            catch (Exception)
            {
                _degraded = true;
            }
        }
    }

    /// <summary>结束会话并固化快照。</summary>
    public GameSessionSnapshot End()
    {
        if (!_started)
        {
            return EmptySnapshot();
        }

        // 最后一次读取，收尾进程退出前刚写入的日志。
        Poll();

        // 探测各日志文件状态。
        var states = new List<LogFileProbeResult>();
        foreach (var source in _sources)
        {
            if (_fileSystem.FileExists(source.Path))
            {
                states.Add(LogFileProbe.Probe(_fileSystem, source.Path, source.Name));
            }
        }

        var snapshot = CreateSnapshot(_clock.UtcNow.ToLocalTime(), states);
        _started = false;
        return snapshot;
    }

    /// <summary>当前会话的实时快照（不结束会话）。</summary>
    public GameSessionSnapshot Snapshot() => CreateSnapshot(null, Array.Empty<LogFileProbeResult>());

    private GameSessionSnapshot CreateSnapshot(
        DateTimeOffset? endTime,
        IReadOnlyList<LogFileProbeResult> logStates) => new(
        _game?.GameId ?? string.Empty,
        _game?.DisplayName ?? string.Empty,
        _pid,
        _startTime,
        endTime,
        _errorBlocks,
        _groups.Values
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Signature, StringComparer.Ordinal)
            .Take(12)
            .Select(static group => new SessionErrorGroup(
                group.Signature, group.Count, group.Source, ModName: null, group.Sample))
            .ToArray(),
        logStates,
        _degraded,
        _fatalInfo);

    private GameSessionSnapshot EmptySnapshot() => new(
        string.Empty,
        string.Empty,
        null,
        _clock.UtcNow.ToLocalTime(),
        _clock.UtcNow.ToLocalTime(),
        0,
        Array.Empty<SessionErrorGroup>(),
        Array.Empty<LogFileProbeResult>(),
        false);

    private void RecordFatal(CrashLogBlock block, string logName)
    {
        if (_fatalInfo is not null)
        {
            return;
        }

        string? category = CrashLogParser.DetectFatalCategory(block.Text);
        if (category is null)
        {
            return;
        }

        string signature = category + "|" + CrashLogParser.Signature(block);
        if (!_seenFatals.Add(signature))
        {
            return;
        }

        _fatalInfo = new SessionFatalInfo(
            category,
            signature,
            logName,
            Truncate(block.Message, 240));
    }

    /// <summary>从字节区间读取文本；处理 UTF-8 截断，返回实际读到的下一偏移。</summary>
    private string ReadRange(string path, long offset, long length, out long nextOffset)
    {
        nextOffset = offset;
        long size = Math.Min(length - offset, MaxReadBytesPerPoll);
        if (size <= 0)
        {
            return string.Empty;
        }

        using var stream = _fileSystem.OpenRead(path);
        if (!stream.CanSeek)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            nextOffset = length;
            return reader.ReadToEnd();
        }

        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[size];
        int read = 0;
        while (read < buffer.Length)
        {
            int chunk = stream.Read(buffer, read, buffer.Length - read);
            if (chunk <= 0)
            {
                break;
            }

            read += chunk;
        }

        // 只推进到完整 UTF-8 字符边界。
        int valid = read;
        while (valid > 0 && (buffer[valid - 1] & 0xC0) == 0x80)
        {
            valid--;
        }

        nextOffset = offset + valid;
        if (valid <= 0)
        {
            return string.Empty;
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        return encoding.GetString(buffer, 0, valid);
    }

    private static string Truncate(string text, int maxLength) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= maxLength ? text : text[..maxLength] + "…";

    private sealed class LogCursor(long offset)
    {
        public long Offset { get; set; } = offset;

        public string Pending { get; set; } = string.Empty;
    }

    private sealed class GroupAccumulator(string signature, string? source, string sample)
    {
        public string Signature { get; } = signature;

        public string? Source { get; set; } = source;

        public string Sample { get; } = sample;

        public int Count { get; set; } = 1;
    }
}
