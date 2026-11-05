namespace PdxLauncher.Checks;

using System.Text;

using PdxLauncher.Core.Steam;

/// <summary>
/// 自检工程兼作「假 Steam 宿主」：被 <c>--steam-host</c> 拉起时走这里。
/// <para>
/// 行为由环境变量 <c>PDXNEXT_FAKE_MODE</c> 控制，默认 <c>sleep</c>（保持旧用例：
/// 挂住不返回，验证调用方取消原样传播）。其余模式用于钉住常驻会话的复用、崩溃重启一次、
/// 坏版本回落、结果码透传与空闲退出。
/// </para>
/// <para>
/// 每次启动把「第几次启动」写到 <c>PDXNEXT_FAKE_STATE</c> 目录下的 <c>starts.txt</c>，
/// 让 <c>crash-first</c> 只崩第一个 worker，后续进程正常应答。
/// </para>
/// </summary>
internal static class FakeSteamHost
{
    public const string ModeVariable = "PDXNEXT_FAKE_MODE";
    public const string StateVariable = "PDXNEXT_FAKE_STATE";

    private static StreamWriter _stdout = null!;

    public static int Run(string[] args)
    {
        _stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 4096)
        {
            AutoFlush = false,
        };

        var verb = args.Length > 1 ? args[1] : string.Empty;
        var mode = (Environment.GetEnvironmentVariable(ModeVariable) ?? "sleep").Trim().ToLowerInvariant();

        if (string.Equals(verb, SteamHostProtocol.ServeVerb, StringComparison.OrdinalIgnoreCase))
        {
            return Serve(args, mode);
        }

        if (string.Equals(verb, SteamHostProtocol.AchievementsVerb, StringComparison.OrdinalIgnoreCase))
        {
            return OneShotAchievements(mode);
        }

        if (string.Equals(verb, SteamHostProtocol.ProbeVerb, StringComparison.OrdinalIgnoreCase))
        {
            Write(SteamHostProtocol.Encode(new SteamHostProbeResult(
                Initialized: true,
                SteamRunning: true,
                SteamId64: 76561190000000000UL,
                Dlcs: [],
                Error: null,
                Detail: "fake")));
            return SteamHostProtocol.ExitOk;
        }

        return SteamHostProtocol.ExitFailure;
    }

    private static int Serve(string[] args, string mode)
    {
        var appId = OptionInt(args, "--appid");
        var apiPath = OptionText(args, "--api") ?? string.Empty;
        var starts = BumpStartCount();

        var version = mode == "bad-version" ? 99 : SteamHostWorkerProtocol.Version;
        Write(SteamHostProtocol.Encode(new SteamHostWorkerHello
        {
            Version = version,
            AppId = appId,
            ApiPath = apiPath,
        }));

        if (mode == "bad-version")
        {
            // 版本不符：握手就作废，App 侧应回落一次性宿主。
            return SteamHostProtocol.ExitOk;
        }

        while (true)
        {
            var line = Console.In.ReadLine();
            if (line is null)
            {
                return SteamHostProtocol.ExitOk;
            }

            if (SteamHostWorkerProtocol.TryParseCommand(line, out var command)
                != SteamHostWorkerParseStatus.Command)
            {
                continue;
            }

            if (SteamHostWorkerProtocol.IsKnownVerb(command.Verb)
                && string.Equals(command.Verb, SteamHostWorkerProtocol.ShutdownVerb, StringComparison.OrdinalIgnoreCase))
            {
                Write(SteamHostProtocol.Encode(SteamHostWorkerReply.Pong() with { Verb = command.Verb }));
                return SteamHostProtocol.ExitOk;
            }

            if (string.Equals(command.Verb, SteamHostWorkerProtocol.PingVerb, StringComparison.OrdinalIgnoreCase))
            {
                Write(SteamHostProtocol.Encode(SteamHostWorkerReply.Pong()));
                continue;
            }

            if (!string.Equals(command.Verb, SteamHostProtocol.AchievementsVerb, StringComparison.OrdinalIgnoreCase))
            {
                Write(SteamHostProtocol.Encode(SteamHostWorkerReply.Fail(command.Verb, SteamAchievementCodes.Unsupported, "fake 不支持")));
                continue;
            }

            switch (mode)
            {
                case "crash-first":
                    if (starts == 1)
                    {
                        // 第一个 worker 在请求中途直接退出：App 侧应重启一次并成功。
                        return 3;
                    }

                    break;

                case "slow-worker":
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
                    break;

                case "bad-payload":
                    _stdout.WriteLine(SteamHostProtocol.ResultPrefix + "{ 这不是 JSON");
                    _stdout.Flush();
                    continue;

                case "ok-delay":
                    System.Threading.Thread.Sleep(150);
                    break;

                case "no-dll":
                    Write(SteamHostProtocol.Encode(SteamHostWorkerReply.FromAchievements(
                        SteamHostProtocol.AchievementsVerb,
                        SteamHostAchievementsResult.Fail(SteamAchievementCodes.NoDll, "找不到 steam_api64.dll：fake"))));
                    continue;
            }

            Write(SteamHostProtocol.Encode(SteamHostWorkerReply.FromAchievements(
                SteamHostProtocol.AchievementsVerb,
                SuccessResult())));
        }
    }

    private static int OneShotAchievements(string mode)
    {
        if (mode == "sleep")
        {
            // 旧用例：挂住不返回，等父进程取消 / 超时。
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(30));
            return SteamHostProtocol.ExitFailure;
        }

        var result = mode == "no-dll"
            ? SteamHostAchievementsResult.Fail(SteamAchievementCodes.NoDll, "找不到 steam_api64.dll：fake")
            : SuccessResult();
        Write(SteamHostProtocol.Encode(result));
        return SteamHostProtocol.ExitOk;
    }

    private static SteamHostAchievementsResult SuccessResult() =>
        SteamHostAchievementsResult.Success(
        [
            new SteamHostAchievementItem
            {
                Name = "ACH_ONE",
                DisplayName = "第一条",
                Description = "假成就一",
                Achieved = true,
                UnlockTime = 1_700_000_000,
                GlobalPercent = 12.3,
            },
            new SteamHostAchievementItem
            {
                Name = "ACH_TWO",
                DisplayName = "第二条",
                Description = "假成就二",
                Achieved = false,
                GlobalPercent = 45.6,
            },
        ]);

    private static int BumpStartCount()
    {
        var directory = Environment.GetEnvironmentVariable(StateVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return 1;
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "starts.txt");
        var next = 1;
        if (File.Exists(path) && int.TryParse(File.ReadAllText(path), out var value))
        {
            next = value + 1;
        }

        File.WriteAllText(path, next.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return next;
    }

    private static int OptionInt(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var value))
            {
                return value;
            }
        }

        return 0;
    }

    private static string? OptionText(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void Write(string line)
    {
        _stdout.WriteLine(line);
        _stdout.Flush();
    }
}
