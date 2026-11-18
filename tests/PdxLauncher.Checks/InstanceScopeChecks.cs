namespace PdxLauncher.Checks;

using PdxLauncher.Core;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 单实例作用域（按程序目录隔离）的纯函数契约。
/// <para>
/// 背景：锁名原来是全局同名的 <c>Local\PdxNext.App</c>，导致同一台机器上装在两个盘的
/// PdxNext 只有一个能开 —— 开发机上表现为「仓库构建出来的 exe 被 D 盘那份顶掉」。
/// 作用域键必须**只由程序目录决定**，并且同一目录的不同写法（大小写、末尾分隔符）折叠成同键。
/// </para>
/// </summary>
public static class InstanceScopeChecks
{
    /// <summary>仓库里构建出来的目录（只用来做「不同目录不同键」的样本，不要求存在）。</summary>
    private const string RepoDir = @"G:\Work\paradox启动器\src\PdxLauncher.App\bin\x64\Release\net11.0-windows10.0.26100.0\win-x64";

    /// <summary>用户实际安装目录（样本；同样不要求存在）。</summary>
    private const string InstalledDir = @"D:\app\PdxNext";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        ChecksContext.Check("单实例作用域: 同一目录两次调用结果相同", ScopeOf(RepoDir) == ScopeOf(RepoDir));

        var lower = ScopeOf(RepoDir.ToLowerInvariant());
        ChecksContext.Equal("单实例作用域: 大小写不敏感", ScopeOf(RepoDir), lower);

        ChecksContext.Equal(
            "单实例作用域: 末尾分隔符不影响键",
            ScopeOf(InstalledDir),
            ScopeOf(InstalledDir + Path.DirectorySeparatorChar));

        ChecksContext.Check(
            "单实例作用域: 仓库构建目录与 D 盘安装目录不同键",
            ScopeOf(RepoDir) != ScopeOf(InstalledDir));

        var scope = ScopeOf(RepoDir);
        ChecksContext.Equal("单实例作用域: 键长固定", PdxInstanceScope.HashLength, scope.Length);
        ChecksContext.Check(
            "单实例作用域: 键只含小写十六进制",
            scope.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
            scope);

        var mutex = PdxInstanceScope.MutexNameFor(RepoDir);
        var pipe = PdxInstanceScope.PipeNameFor(RepoDir);
        ChecksContext.Check(
            "单实例作用域: 锁名带前缀",
            mutex.StartsWith(PdxInstanceScope.MutexNamePrefix + ".", StringComparison.Ordinal),
            mutex);
        ChecksContext.Check(
            "单实例作用域: 管道名带前缀",
            pipe.StartsWith(PdxInstanceScope.PipeNamePrefix + ".", StringComparison.Ordinal),
            pipe);
        ChecksContext.Check(
            "单实例作用域: 作用域锁名不等于安装器标记名",
            !string.Equals(mutex, PdxInstanceScope.InstallerMutexName, StringComparison.Ordinal),
            mutex);
        ChecksContext.Check(
            "单实例作用域: 不同目录的锁名不同",
            PdxInstanceScope.MutexNameFor(RepoDir) != PdxInstanceScope.MutexNameFor(InstalledDir));

        // Mutex 名上限 260 字符、管道名上限 256：前缀 + 1 + 16 远低于两者，这里钉住别写长。
        ChecksContext.Check("单实例作用域: 锁名长度远低于上限", mutex.Length < 64, mutex.Length + " 字符");
        ChecksContext.Check("单实例作用域: 管道名长度远低于上限", pipe.Length < 64, pipe.Length + " 字符");

        ChecksContext.Equal(
            "单实例作用域: 安装器 AppMutex 名字未改",
            @"Local\PdxNext.App",
            PdxInstanceScope.InstallerMutexName);

        // 非法输入必须抛，而不是悄悄退化成全局同名（那会把「多盘并存」再堵回去）。
        ChecksContext.Check("单实例作用域: 空路径抛 ArgumentException", ThrowsOnEmpty());

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static string ScopeOf(string directory) => PdxInstanceScope.ScopeFor(directory);

    private static bool ThrowsOnEmpty()
    {
        try
        {
            _ = PdxInstanceScope.ScopeFor("   ");
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
