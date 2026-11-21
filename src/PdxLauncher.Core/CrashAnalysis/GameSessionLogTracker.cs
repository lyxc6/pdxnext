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
/// </para>
/// </summary>
public sealed class GameSessionLogTracker
{
    /// <summary>与 GameCrashAnalyzer 相同的日志清单。</summary>
    private static readonly string[] LogFileNames =
    [
        "error.log", "game.log", "debug.log", "system.log", "graphics.log", "warning.log", "database_conflicts.log",
    ];

    private const int MaxReadBytesPerPoll = 2 * 1024 * 1024;

    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;

    private GameInstallation? _game;
    private string? _logDirectory;
    private int? _pid;
    private DateTimeOffset _startTime;
    private bool _started;
    private bool _degraded;
    private readonly Dictionary<string, LogCursor> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupAccumulator> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenErrors = new(StringComparer.Ordinal);
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
        _logDirectory = string.IsNullOrWhiteSpace(game.GameDataDir)
            ? string.Empty
            : _fileSystem.Combine(game.GameDataDir, "logs");

        _cursors.Clear();
        _groups.Clear();
        _seenErrors.Clear();
        _errorBlocks = 0;
        _degraded = false;
        _started = true;

        // 从当前文件末尾开始，避免把上次会话遗留的日志算进来。
        foreach (string name in LogFileNames)
        {
            string path = string.IsNullOrWhiteSpace(_logDirectory)
                ? string.Empty
                : _fileSystem.Combine(_logDirectory, name);
            if (string.IsNullOrWhiteSpace(path) || !_fileSystem.FileExists(path))
            {
                continue;
            }

            try
            {
                _cursors[name] = new LogCursor(_fileSystem.GetFileSize(path));
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
        if (!_started || string.IsNullOrWhiteSpace(_logDirectory))
        {
            return;
        }

        foreach (string name in LogFileNames)
        {
            string path = _fileSystem.Combine(_logDirectory, name);
            if (!_fileSystem.FileExists(path))
            {
                continue;
            }

            try
            {
                if (!_cursors.TryGetValue(name, out var cursor))
                {
                    // 会话期间新建的日志：从 0 开始读，但只统计会话时间之后的块。
                    cursor = new LogCursor(0);
                    _cursors[name] = cursor;
                }

                long length = _fileSystem.GetFileSize(path);
                if (length <= cursor.Offset)
                {
                    if (length < cursor.Offset)
                    {
                        // 日志被轮转/截断：从头再来。
                        cursor.Offset = 0;
                        cursor.Pending = string.Empty;
                    }
                    else
                    {
                        continue;
                    }
                }

                string text = ReadRange(path, cursor.Offset, length, out long nextOffset);
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
                    if (!CrashLogParser.IsErrorBlock(block))
                    {
                        continue;
                    }

                    // 只统计会话开始之后写入的块（HH:mm:ss 已锚定到起始日）。
                    if (block.Time is { } time && time < _startTime.AddSeconds(-2))
                    {
                        continue;
                    }

                    string key = block.Time?.UtcTicks.ToString(CultureInfo.InvariantCulture) + "|"
                        + block.Origin + "|" + block.Message;
                    if (!_seenErrors.Add(key))
                    {
                        continue; // 同一错误在 error/game/debug 里重复出现只算一次。
                    }

                    _errorBlocks++;
                    string signature = CrashLogParser.Signature(block);
                    string? source = CrashLogParser.ExtractSourcePath(block.Text);
                    if (!_groups.TryGetValue(signature, out var group))
                    {
                        group = new GroupAccumulator(signature, source, Truncate(block.Message, 220));
                        _groups[signature] = group;
                    }
                    else
                    {
                        group.Count++;
                        group.Source ??= source;
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
        if (!string.IsNullOrWhiteSpace(_logDirectory))
        {
            foreach (string name in LogFileNames)
            {
                string path = _fileSystem.Combine(_logDirectory, name);
                if (_fileSystem.FileExists(path))
                {
                    states.Add(LogFileProbe.Probe(_fileSystem, path, name));
                }
            }
        }

        var groups = _groups.Values
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Signature, StringComparer.Ordinal)
            .Take(12)
            .Select(static group => new SessionErrorGroup(
                group.Signature, group.Count, group.Source, ModName: null, group.Sample))
            .ToArray();

        var snapshot = new GameSessionSnapshot(
            _game?.GameId ?? string.Empty,
            _game?.DisplayName ?? string.Empty,
            _pid,
            _startTime,
            _clock.UtcNow.ToLocalTime(),
            _errorBlocks,
            groups,
            states,
            _degraded);

        _started = false;
        return snapshot;
    }

    /// <summary>当前会话的实时快照（不结束会话）。</summary>
    public GameSessionSnapshot Snapshot() => new(
        _game?.GameId ?? string.Empty,
        _game?.DisplayName ?? string.Empty,
        _pid,
        _startTime,
        null,
        _errorBlocks,
        _groups.Values
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.Signature, StringComparer.Ordinal)
            .Take(12)
            .Select(static group => new SessionErrorGroup(
                group.Signature, group.Count, group.Source, ModName: null, group.Sample))
            .ToArray(),
        Array.Empty<LogFileProbeResult>(),
        _degraded);

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
