namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 导航重入与重试的源码断言（D22）。
/// <para>
/// <c>Frame.Navigate</c> 返回 false 只在真机上偶发（同一帧内上一次导航还没走完），
/// 控制台测不到；但把「重入排队」「下一帧重试一次」「CurrentTag 只在成功时更新」
/// 写丢，是重排代码时很容易发生的回归，所以用源码契约钉住。
/// </para>
/// </summary>
public static class NavigationRetryChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var path = Path.Combine(
            FindWorkspaceRoot(),
            "src",
            "PdxLauncher.App",
            "Services",
            "NavigationService.cs");
        if (!ChecksContext.Check("导航重试: NavigationService.cs 存在", File.Exists(path), path))
        {
            return 1;
        }

        var source = File.ReadAllText(path);

        ChecksContext.Check(
            "导航重试: 有重入闸",
            source.Contains("_navigating", StringComparison.Ordinal)
                && source.Contains("_pending", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航重试: 重入时只保留最新请求",
            source.Contains("PendingNavigation", StringComparison.Ordinal)
                && source.Contains("_pending = new PendingNavigation", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航重试: 失败后下一帧重试",
            source.Contains("DispatcherQueuePriority.Low", StringComparison.Ordinal)
                && source.Contains("_retryScheduled", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航重试: 最多重试两次后放弃",
            source.Contains("MaxRetryAttempts", StringComparison.Ordinal)
                && source.Contains("attempt >= MaxRetryAttempts", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航重试: DrainPending 不再同步重入",
            source.Contains("_retryScheduled || _pending is not", StringComparison.Ordinal)
                && source.Contains("DispatcherQueuePriority.Low", StringComparison.Ordinal)
                && source.Contains("NavigateCore(pending.Tag, pending.Parameter, pending.Attempt)", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航重试: 同页短路也会让旧重试失效",
            source.IndexOf("var version = unchecked(++_requestVersion);", StringComparison.Ordinal)
                < source.IndexOf("string.Equals(CurrentTag, tag", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航重试: 旧代号的重试会放弃",
            source.Contains("version != _requestVersion", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航重试: CurrentTag 只在成功时更新",
            source.Contains("if (navigated)", StringComparison.Ordinal)
                && source.Contains("CurrentTag = tag;", StringComparison.Ordinal));
        ChecksContext.Check(
            "导航重试: 失败日志写明安排了重试",
            source.Contains("安排下一帧重试", StringComparison.Ordinal));

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("找不到仓库根（PdxLauncher.sln）");
    }
}
