namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Updates;

/// <summary>
/// 更新包下载的离线自检：正常落位、改名被占用时保留并复用 <c>.part</c>、校验失败清理半成品。
/// 网络层用假 fetcher 顶替；这里专守「写句柄必须先关、改名要重试、完整包不能删」三条。
/// </summary>
public static class UpdateDownloadChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckNormalDownload();
        CheckLockedTargetKeepsAndReusesPart();
        CheckBadHashDropsPart();
        CheckTransientFailureRetries();
        CheckAllAttemptsFailCleanup();
        CheckThrottleDoesNotRetry();
        CheckCancellationDoesNotRetry();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckNormalDownload()
    {
        Console.WriteLine("[UpdateDownloadChecks] 正常下载落位");
        var root = NewRoot();
        try
        {
            var payload = Payload();
            var fetcher = new FakeUpdateFetcher(payload);
            var service = NewService(root, fetcher);
            var manifest = Manifest(payload);
            var progress = new CollectingProgress();

            var path = service.DownloadAsync(manifest, progress, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Equal("返回正式安装包路径", Path.Combine(root, "PdxNext-Setup-1.4.1.exe"), path);
            ChecksContext.Check("正式安装包存在", File.Exists(path));
            ChecksContext.EqualBytes("安装包内容一致", payload, File.ReadAllBytes(path));
            ChecksContext.Check(".part 已不存在", !File.Exists(path + ".part"));
            ChecksContext.Equal("下载只发起一次", 1, fetcher.GetDownloadCallCount);
            ChecksContext.Check("进度最后报满", progress.Samples.Count > 0 && progress.Samples[^1].BytesReceived == payload.Length);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void CheckLockedTargetKeepsAndReusesPart()
    {
        Console.WriteLine("[UpdateDownloadChecks] 目标被占用：保留 .part 并在下次复用");
        var root = NewRoot();
        try
        {
            var payload = Payload();
            var fetcher = new FakeUpdateFetcher(payload);
            var service = NewService(root, fetcher);
            var manifest = Manifest(payload);
            var finalPath = Path.Combine(root, "PdxNext-Setup-1.4.1.exe");

            // 用一个不共享删除的句柄占住目标文件，模拟杀毒 / 资源管理器短暂锁定安装包。
            using (var lockStream = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                lockStream.WriteByte(0x00);
                lockStream.Flush();

                var thrown = false;
                try
                {
                    service.DownloadAsync(manifest, null, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (UpdatePackageLockedException)
                {
                    thrown = true;
                }

                ChecksContext.Check("三次改名失败后抛 UpdatePackageLockedException", thrown);
                ChecksContext.Check("失败的 .part 仍保留", File.Exists(finalPath + ".part"));
                ChecksContext.EqualBytes("保留的 .part 内容完整", payload, File.ReadAllBytes(finalPath + ".part"));
                ChecksContext.Equal("正式文件未被覆盖", 1, new FileInfo(finalPath).Length);
            }

            // 释放锁后重试：应直接复用已校验的 .part，不再发起 HTTP 下载。
            var path = service.DownloadAsync(manifest, null, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Equal("复用后返回正式安装包路径", finalPath, path);
            ChecksContext.EqualBytes("复用后安装包内容一致", payload, File.ReadAllBytes(path));
            ChecksContext.Check("复用后 .part 已落位删除", !File.Exists(finalPath + ".part"));
            ChecksContext.Equal("复用没有重新下载", 1, fetcher.GetDownloadCallCount);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void CheckBadHashDropsPart()
    {
        Console.WriteLine("[UpdateDownloadChecks] 校验失败清理半成品");
        var root = NewRoot();
        try
        {
            var payload = Payload();
            var service = NewService(root, new FakeUpdateFetcher(payload));
            var manifest = Manifest(payload, badHash: true);

            var thrown = false;
            try
            {
                service.DownloadAsync(manifest, null, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (InvalidDataException)
            {
                thrown = true;
            }

            ChecksContext.Check("哈希不符抛 InvalidDataException", thrown);
            ChecksContext.Check("哈希不符时 .part 被清理", !File.Exists(Path.Combine(root, "PdxNext-Setup-1.4.1.exe.part")));
            ChecksContext.Check("哈希不符时没有正式安装包", !File.Exists(Path.Combine(root, "PdxNext-Setup-1.4.1.exe")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static UpdateService NewService(string root, FakeUpdateFetcher fetcher) =>
        new(
            fetcher,
            new SilentLogger(),
            new AppSettings(),
            root,
            root,
            "1.4.0")
        {
            MoveRetryDelay = TimeSpan.Zero,
            DownloadRetryDelay = TimeSpan.Zero,
        };

    private static void CheckTransientFailureRetries()
    {
        Console.WriteLine("[UpdateDownloadChecks] 前两次瞬时失败后第三次成功");
        var root = NewRoot();
        try
        {
            var payload = Payload();
            var fetcher = new FakeUpdateFetcher(
                payload,
                attempt => attempt < 3
                    ? new System.Net.Http.HttpRequestException("离线测试：连接被重置")
                    : null);
            var service = NewService(root, fetcher);
            var manifest = Manifest(payload);

            var path = service.DownloadAsync(manifest, null, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Equal("瞬时失败: 第 3 次尝试成功", 3, fetcher.GetDownloadCallCount);
            ChecksContext.EqualBytes("瞬时失败后内容一致", payload, File.ReadAllBytes(path));
            ChecksContext.Check("瞬时失败后没有 .part 残留", !File.Exists(path + ".part"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void CheckAllAttemptsFailCleanup()
    {
        Console.WriteLine("[UpdateDownloadChecks] 三次全失败：清理 .part 并抛出原异常");
        var root = NewRoot();
        try
        {
            var payload = Payload();
            var finalPath = Path.Combine(root, "PdxNext-Setup-1.4.1.exe");
            var fetcher = new FakeUpdateFetcher(
                payload,
                _ => new System.Net.Http.HttpRequestException("离线测试：TLS 握手失败"));
            var service = NewService(root, fetcher);
            var manifest = Manifest(payload);

            var thrown = false;
            try
            {
                service.DownloadAsync(manifest, null, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (System.Net.Http.HttpRequestException)
            {
                thrown = true;
            }

            ChecksContext.Check("三次全失败: 抛 HttpRequestException", thrown);
            ChecksContext.Equal("三次全失败: 正好尝试 3 次", UpdateService.DownloadAttempts, fetcher.GetDownloadCallCount);
            ChecksContext.Check("三次全失败: .part 被清理", !File.Exists(finalPath + ".part"));
            ChecksContext.Check("三次全失败: 没有正式安装包", !File.Exists(finalPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void CheckThrottleDoesNotRetry()
    {
        Console.WriteLine("[UpdateDownloadChecks] 被限流不重试");
        var root = NewRoot();
        try
        {
            var payload = Payload();
            var fetcher = new FakeUpdateFetcher(
                payload,
                _ => new PdxLauncher.Core.Online.SteamThrottleException(
                    "github.com",
                    TimeSpan.FromSeconds(30),
                    rejectedByCooldown: false,
                    "离线测试：429"));
            var service = NewService(root, fetcher);

            var thrown = false;
            try
            {
                service.DownloadAsync(Manifest(payload), null, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (PdxLauncher.Core.Online.SteamThrottleException)
            {
                thrown = true;
            }

            ChecksContext.Check("限流: 抛出原异常", thrown);
            ChecksContext.Equal("限流: 只尝试 1 次", 1, fetcher.GetDownloadCallCount);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void CheckCancellationDoesNotRetry()
    {
        Console.WriteLine("[UpdateDownloadChecks] 用户取消不重试");
        var root = NewRoot();
        try
        {
            var payload = Payload();
            var fetcher = new FakeUpdateFetcher(payload);
            var service = NewService(root, fetcher);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var thrown = false;
            try
            {
                service.DownloadAsync(Manifest(payload), null, cancellation.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                thrown = true;
            }

            ChecksContext.Check("取消: 抛出 OperationCanceledException", thrown);
            ChecksContext.Equal("取消: 不发起下载", 0, fetcher.GetDownloadCallCount);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static UpdateManifest Manifest(byte[] payload, bool badHash = false) => new()
    {
        Version = "1.4.1",
        SetupUrl = "https://github.com/lyxc6/pdxnext/releases/download/v1.4.1/PdxNext-Setup-1.4.1.exe",
        Sha256 = badHash
            ? new string('0', 64)
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(),
        Size = payload.Length,
    };

    private static byte[] Payload() => System.Text.Encoding.ASCII.GetBytes("pdxnext-update-check-payload-141");

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-update-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果。
        }
    }

    private sealed class FakeUpdateFetcher : IHttpFetcher, IDownloadFetcher
    {
        private readonly byte[] _payload;
        private readonly Func<int, Exception?>? _failOn;

        public FakeUpdateFetcher(byte[] payload, Func<int, Exception?>? failOn = null)
        {
            _payload = payload;
            _failOn = failOn;
        }

        public int GetDownloadCallCount { get; private set; }

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken) =>
            throw new NotSupportedException("更新下载只走 GetDownloadAsync");

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException("更新下载不使用 POST");

        public Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException("更新下载不使用 POST");

        public Task<HttpDownload> GetDownloadAsync(Uri uri, long? startOffset, CancellationToken cancellationToken)
        {
            GetDownloadCallCount++;
            if (_failOn?.Invoke(GetDownloadCallCount) is { } failure)
            {
                throw failure;
            }

            return Task.FromResult(new HttpDownload(new MemoryStream(_payload, writable: false), _payload.Length));
        }
    }

    private sealed class CollectingProgress : IProgress<UpdateProgress>
    {
        public List<UpdateProgress> Samples { get; } = new();

        public void Report(UpdateProgress value) => Samples.Add(value);
    }

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
