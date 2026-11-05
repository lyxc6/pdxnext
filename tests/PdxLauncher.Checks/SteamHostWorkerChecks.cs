namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 常驻宿主（<c>--steam-host serve</c>）的行协议与命令循环。
/// <para>
/// 这里钉住的是「便宜但致命」的几处：空行不能回包、坏 JSON 只能回一行 requestFailed 不能退出、
/// 未知动词回 unsupported 不能杀进程、结果码必须原样透传、空闲超时必须自行退出。
/// 协议错位不会崩，只会让成就页静默走回落或一直转圈，编译器看不见。
/// </para>
/// </summary>
public static class SteamHostWorkerChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        CheckParsing();
        CheckKnownVerbsAndTimeouts();
        CheckEndToEnd();
        CheckEofExits();
        CheckIdleTimeout();
        return 0;
    }

    private static void CheckParsing()
    {
        ChecksContext.Equal(
            "worker 协议：空行忽略",
            SteamHostWorkerParseStatus.Ignore,
            SteamHostWorkerProtocol.TryParseCommand("   ", out _));

        ChecksContext.Equal(
            "worker 协议：null 行忽略",
            SteamHostWorkerParseStatus.Ignore,
            SteamHostWorkerProtocol.TryParseCommand(null, out _));

        var status = SteamHostWorkerProtocol.TryParseCommand("achievements 1500", out var bare);
        ChecksContext.Equal("worker 协议：裸动词可解析", SteamHostWorkerParseStatus.Command, status);
        ChecksContext.Equal("worker 协议：裸动词为 achievements", SteamHostProtocol.AchievementsVerb, bare.Verb);
        ChecksContext.Equal("worker 协议：裸动词超时透传", 1500, bare.TimeoutMs);

        status = SteamHostWorkerProtocol.TryParseCommand("{\"verb\":\"ping\",\"timeoutMs\":250}", out var json);
        ChecksContext.Equal("worker 协议：JSON 命令可解析", SteamHostWorkerParseStatus.Command, status);
        ChecksContext.Equal("worker 协议：JSON 动词为 ping", SteamHostWorkerProtocol.PingVerb, json.Verb);
        ChecksContext.Equal("worker 协议：JSON 超时透传", 250, json.TimeoutMs);

        ChecksContext.Equal(
            "worker 协议：坏 JSON 判 Malformed",
            SteamHostWorkerParseStatus.Malformed,
            SteamHostWorkerProtocol.TryParseCommand("{\"verb\":", out _));

        ChecksContext.Equal(
            "worker 协议：JSON 缺动词判 Malformed",
            SteamHostWorkerParseStatus.Malformed,
            SteamHostWorkerProtocol.TryParseCommand("{\"timeoutMs\":100}", out _));

        ChecksContext.Equal(
            "worker 协议：非数字超时判 Malformed",
            SteamHostWorkerParseStatus.Malformed,
            SteamHostWorkerProtocol.TryParseCommand("achievements abc", out _));

        var reply = new SteamHostWorkerReply
        {
            Verb = SteamHostProtocol.AchievementsVerb,
            Code = SteamAchievementCodes.NoDll,
            Ok = false,
            Error = "找不到 steam_api64.dll",
        };
        ChecksContext.Check(
            "worker 协议：回复可编码解码",
            SteamHostProtocol.TryDecode(SteamHostProtocol.Encode(reply), out SteamHostWorkerReply? decoded)
            && decoded is not null
            && decoded.Code == SteamAchievementCodes.NoDll
            && decoded.Verb == SteamHostProtocol.AchievementsVerb);
    }

    private static void CheckKnownVerbsAndTimeouts()
    {
        ChecksContext.Check("worker 动词：achievements 认识", SteamHostWorkerProtocol.IsKnownVerb(SteamHostProtocol.AchievementsVerb));
        ChecksContext.Check("worker 动词：ping 认识", SteamHostWorkerProtocol.IsKnownVerb(SteamHostWorkerProtocol.PingVerb));
        ChecksContext.Check("worker 动词：shutdown 认识", SteamHostWorkerProtocol.IsKnownVerb(SteamHostWorkerProtocol.ShutdownVerb));
        ChecksContext.Check("worker 动词：workshop-publish 不认识", !SteamHostWorkerProtocol.IsKnownVerb(SteamHostProtocol.WorkshopPublishVerb));
        ChecksContext.Check("worker 动词：null 不认识", !SteamHostWorkerProtocol.IsKnownVerb(null));

        var fallback = TimeSpan.FromSeconds(10);
        ChecksContext.Equal(
            "worker 超时：0 用默认",
            fallback,
            SteamHostWorkerProtocol.ResolveTimeout(0, fallback));
        ChecksContext.Equal(
            "worker 超时：正数透传",
            TimeSpan.FromMilliseconds(750),
            SteamHostWorkerProtocol.ResolveTimeout(750, fallback));
        ChecksContext.Equal(
            "worker 超时：超过上限被钳位",
            SteamHostWorkerProtocol.MaxRequestTimeout,
            SteamHostWorkerProtocol.ResolveTimeout((int)TimeSpan.FromHours(1).TotalMilliseconds, fallback));
    }

    private static void CheckEndToEnd()
    {
        const string input = "\nping\n{\nnot-a-verb\nachievements 250\nshutdown\n";
        var reader = new FakeReader(2);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = SteamHostWorker.Run(
            @"C:\fake\steam_api64.dll",
            424242,
            new StringReader(input),
            stdout,
            stderr,
            TimeSpan.FromSeconds(5),
            (_, _) => reader);

        ChecksContext.Equal("worker 端到端：正常退出码", SteamHostProtocol.ExitOk, exit);

        var lines = stdout.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToList();

        ChecksContext.Check("worker 端到端：先写握手行", lines.Count >= 1);
        ChecksContext.Check(
            "worker 端到端：握手含 appid",
            SteamHostProtocol.TryDecode(lines[0], out SteamHostWorkerHello? hello)
            && hello is not null
            && hello.AppId == 424242
            && hello.Version == SteamHostWorkerProtocol.Version);

        var replies = lines
            .Skip(1)
            .Select(line => SteamHostProtocol.TryDecode(line, out SteamHostWorkerReply? reply) ? reply : null)
            .ToList();

        ChecksContext.Equal("worker 端到端：空行不回包", 5, replies.Count);
        ChecksContext.Check(
            "worker 端到端：ping 回 ok",
            replies[0] is { Ok: true, Code: SteamAchievementCodes.Ok });
        ChecksContext.Check(
            "worker 端到端：坏 JSON 回 requestFailed",
            replies[1] is { Ok: false } && replies[1]!.Code == SteamAchievementCodes.RequestFailed);
        ChecksContext.Check(
            "worker 端到端：未知动词回 unsupported",
            replies[2] is { Ok: false } && replies[2]!.Code == SteamAchievementCodes.Unsupported);
        ChecksContext.Check(
            "worker 端到端：成就结果码与条目透传",
            replies[3] is { Ok: true, Code: SteamAchievementCodes.Ok }
            && replies[3]!.Achievements is { Items.Count: 2 });
        ChecksContext.Check(
            "worker 端到端：shutdown 回 ok",
            replies[4] is { Ok: true } && replies[4]!.Verb == SteamHostWorkerProtocol.ShutdownVerb);
        ChecksContext.Equal("worker 端到端：只读一次成就", 1, reader.Calls);
        ChecksContext.Check("worker 端到端：退出时释放原生会话", reader.Disposed);
    }

    private static void CheckEofExits()
    {
        var stdout = new StringWriter();
        var exit = SteamHostWorker.Run(
            @"C:\fake\steam_api64.dll",
            42,
            new StringReader(string.Empty),
            stdout,
            new StringWriter(),
            TimeSpan.FromSeconds(5),
            (_, _) => new FakeReader(0));

        ChecksContext.Equal("worker EOF：退出码 ok", SteamHostProtocol.ExitOk, exit);
        ChecksContext.Check("worker EOF：仍先写握手行", stdout.ToString().Contains("PDXNEXT-STEAM-HOST", StringComparison.Ordinal));
    }

    private static void CheckIdleTimeout()
    {
        var stderr = new StringWriter();
        var started = DateTime.UtcNow;
        var exit = SteamHostWorker.Run(
            @"C:\fake\steam_api64.dll",
            42,
            new BlockingReader(),
            new StringWriter(),
            stderr,
            TimeSpan.FromMilliseconds(200),
            (_, _) => new FakeReader(0));

        ChecksContext.Equal("worker 空闲：超时后退出码 ok", SteamHostProtocol.ExitOk, exit);
        ChecksContext.Check(
            "worker 空闲：stderr 留下原因",
            stderr.ToString().Contains("空闲", StringComparison.Ordinal));
        ChecksContext.Check(
            "worker 空闲：没等满 10 分钟",
            DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    private sealed class FakeReader : ISteamHostWorkerAchievements
    {
        private readonly int _items;

        public FakeReader(int items) => _items = items;

        public int Calls { get; private set; }

        public bool Disposed { get; private set; }

        public SteamHostAchievementsResult ReadAchievements(TimeSpan timeout)
        {
            Calls++;
            var list = new List<SteamHostAchievementItem>(_items);
            for (var i = 0; i < _items; i++)
            {
                list.Add(new SteamHostAchievementItem { Name = "A" + i, DisplayName = "成就" + i });
            }

            return SteamHostAchievementsResult.Success(list);
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>永不返回的 stdin：用来触发「空闲超时」而不是 EOF。</summary>
    private sealed class BlockingReader : TextReader
    {
        public override string? ReadLine()
        {
            Thread.Sleep(TimeSpan.FromSeconds(3));
            return null;
        }
    }
}
