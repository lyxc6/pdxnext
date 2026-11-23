namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 已退订 Steam 工坊 mod 的本机清理自检：仓储删除只匹配工坊 id，内容目录只匹配
/// <c>steamapps\workshop\content\&lt;appid&gt;\&lt;workshopId&gt;</c>。
/// </summary>
public static class SubscribedModCleanupChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckOwnRepositoryDelete(fs);
        CheckExactWorkshopPath(fs);
        CheckViewModelContract(fs);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckOwnRepositoryDelete(IFileSystem fs)
    {
        Console.WriteLine("[SubscribedModCleanupChecks] 自有库按工坊 id 删除");
        var root = PlaysetChecks.CreateTempRoot(fs, "subscribed-cleanup-repo");
        try
        {
            var logger = new NullLogger();
            var clock = SystemClock.Instance;
            var dataDir = Path.Combine(root, "documents", "ck3");
            fs.EnsureDirectory(dataDir);
            var game = PlaysetChecks.CreateGame(
                "ck3",
                Path.Combine(root, "install", "ck3"),
                LoadFileFormat.ContentLoadV1_1,
                dataDir,
                Path.Combine(root, "install", "ck3", "game", "dlc"),
                Path.Combine(dataDir, "mod"),
                steamAppId: 1158310);
            var database = new PdxDatabase(fs, logger, Path.Combine(root, "own.sqlite"));
            var repository = new OwnDatabasePlaySetRepository(database, logger, clock);
            var playSetId = repository.CreatePlaySetAsync(game, "PS", CancellationToken.None)
                .GetAwaiter().GetResult().Value;

            var target = Mod("target", Guid.NewGuid(), "steam", "2517153104", "mod/ugc_2517153104.mod");
            var other = Mod("other", Guid.NewGuid(), "steam", "9999999999", string.Empty);
            var local = Mod("local", Guid.NewGuid(), "local", null, string.Empty);
            var saved = repository.SavePlaySetModsAsync(
                game, playSetId, [target, other, local], CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("清理前写入仓储成功", saved.IsSuccess, saved.ErrorMessage);

            var invalid = repository.RemoveWorkshopModAsync(game, "abc", CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("非数字工坊 id 被拒绝", !invalid.IsSuccess);

            var removed = repository.RemoveWorkshopModAsync(game, "2517153104", CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("删除仓储行成功", removed.IsSuccess, removed.ErrorMessage);
            ChecksContext.Equal("只删一行", 1, removed.Value);

            var library = repository.GetModLibraryAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("剩余两个 mod", 2, library.Count);
            ChecksContext.Check("目标工坊 mod 已消失", !library.Any(entry => entry.ModId == target.ModId));
            ChecksContext.Check("其它工坊 mod 保留", library.Any(entry => entry.ModId == other.ModId));
            ChecksContext.Check("本地 mod 保留", library.Any(entry => entry.ModId == local.ModId));
            var remaining = repository.GetPlaySetModsAsync(game, playSetId, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Equal("播放集引用同步删除", 2, remaining.Count);
            ChecksContext.Check("播放集没有目标引用", !remaining.Any(mod => mod.ModId == target.ModId));
        }
        finally
        {
            TryDelete(fs, root);
        }
    }

    private static void CheckExactWorkshopPath(IFileSystem fs)
    {
        Console.WriteLine("[SubscribedModCleanupChecks] 只删精确工坊内容目录");
        var root = PlaysetChecks.CreateTempRoot(fs, "subscribed-cleanup-paths");
        try
        {
            var target = Path.Combine(root, "steamapps", "workshop", "content", "1158310", "2517153104");
            var otherId = Path.Combine(root, "steamapps", "workshop", "content", "1158310", "9999999999");
            var otherApp = Path.Combine(root, "steamapps", "workshop", "content", "529340", "2517153104");
            var local = Path.Combine(root, "steamapps", "common", "victoria3", "mod", "2517153104");
            foreach (var path in new[] { target, otherId, otherApp, local })
            {
                fs.EnsureDirectory(path);
                fs.WriteAllTextAtomic(Path.Combine(path, "descriptor.mod"), "name=\"probe\"");
            }

            var exactTarget = Path.Combine(root, "steamapps", "workshop", "content", "1158310", "2517153104");
            ChecksContext.Check("精确工坊路径构造正确", string.Equals(exactTarget, target, StringComparison.OrdinalIgnoreCase));
            fs.Delete(target);
            ChecksContext.Check("目标目录不存在", !fs.DirectoryExists(target));
            ChecksContext.Check("同游戏其他工坊 id 保留", fs.DirectoryExists(otherId));
            ChecksContext.Check("其他游戏的同 id 保留", fs.DirectoryExists(otherApp));
            ChecksContext.Check("本地 mod 目录保留", fs.DirectoryExists(local));

        }
        finally
        {
            TryDelete(fs, root);
        }
    }

    private static void CheckViewModelContract(IFileSystem fs)
    {
        var root = FindWorkspaceRoot();
        var vmPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopDetailViewModel.cs");
        if (!ChecksContext.Check("详情 VM 存在", File.Exists(vmPath), vmPath))
        {
            return;
        }

        var source = File.ReadAllText(vmPath);
        var start = source.IndexOf("public async Task<WorkshopSubscribeResult> ToggleSteamSubscriptionAsync", StringComparison.Ordinal);
        var end = source.IndexOf("/// <summary>把 acf / Steamworks 读到的状态落到界面上。", StringComparison.Ordinal);
        if (!ChecksContext.Check("找到退订方法", start >= 0 && end > start))
        {
            return;
        }

        var body = source[start..end];
        ChecksContext.Check("只有退订分支调用清理服务", body.Contains("if (!subscribe && _subscribedModCleanup is not null)", StringComparison.Ordinal));
        var successIndex = body.IndexOf("if (result.Ok)", StringComparison.Ordinal);
        var cleanupIndex = body.IndexOf("_subscribedModCleanup", StringComparison.Ordinal);
        ChecksContext.Check(
            "清理在退订成功后执行",
            successIndex >= 0 && cleanupIndex > successIndex,
            "success=" + successIndex + ", cleanup=" + cleanupIndex);
        ChecksContext.Check("失败时返回错误而不是清理警告", body.Contains("return WorkshopSubscribeResult.Fail", StringComparison.Ordinal));
    }

    private static PlaySetMod Mod(string name, Guid id, string source, string? steam, string registry) => new(
        ModId: id,
        Name: name,
        DirPath: "mod/" + name,
        ArchivePath: null,
        GameRegistryId: registry,
        ThumbnailPath: null,
        RequiredVersion: string.Empty,
        Status: "ready_to_play",
        Enabled: true,
        Position: 0,
        SteamId: steam,
        Source: source);

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PdxLauncher.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void TryDelete(IFileSystem fs, string path)
    {
        try
        {
            if (fs.DirectoryExists(path))
            {
                fs.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class NullLogger : IAppLogger
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
