namespace PdxLauncher.Core;

using System;
using System.IO;

/// <summary>
/// 单实例作用域：把「单实例」限定到一个安装目录，而不是整台机器。
/// <para>
/// 原来锁和激活管道都是全局同名（<c>Local\PdxNext.App</c> / <c>PdxNext.App.Activate</c>），
/// 后果是：同一台机器上装在两个盘的 PdxNext 只有一个能开 —— 后启动的那份拿不到锁，
/// 只会把先启动的窗口叫到前台然后退出。开发机上尤其明显：仓库里构建出来的 exe
/// 被用户装在 <c>D:\app\PdxNext</c> 的那份顶掉，看着像「改的代码没生效」。
/// </para>
/// <para>
/// 所以作用域键取**程序目录**（<see cref="AppContext.BaseDirectory"/> 的等价物）：
/// 同一目录内仍然只允许一个实例（防止两个进程并发写同一个 <c>data</c> 目录里的 SQLite 与设置），
/// 不同目录互不影响。
/// </para>
/// <para>
/// 安装器那边不跟着改：<c>installer/PdxNext.iss</c> 的 <c>AppMutex</c> 仍是
/// <see cref="InstallerMutexName"/>，这个全局同名 mutex 保留下来只作为「有副本在运行」的
/// 存在性标记 —— 多个副本同时创建它不会争用（只要持有句柄、不要所有权），
/// 安装器却仍能在安装/卸载前要求关掉所有正在运行的副本。
/// </para>
/// </summary>
public static class PdxInstanceScope
{
    /// <summary>作用域锁的名字前缀；完整名字 = 前缀 + "." + <see cref="ScopeFor"/> 的结果。</summary>
    public const string MutexNamePrefix = @"Local\PdxNext.App";

    /// <summary>同会话内激活管道的名字前缀（NamedPipe 自带会话内可见性，不加 Local 前缀）。</summary>
    public const string PipeNamePrefix = "PdxNext.App.Activate";

    /// <summary>
    /// 安装器 <c>AppMutex</c> 读的就是这个名字，**不能改**：它由
    /// <c>installer/PdxNext.iss</c> 与本文件的常量共同钉住，改一边就断。
    /// <para>语义：仅「存在性标记」，不承担互斥。</para>
    /// </summary>
    public const string InstallerMutexName = @"Local\PdxNext.App";

    /// <summary>作用域哈希的长度（16 位十六进制）。</summary>
    public const int HashLength = 16;

    /// <summary>
    /// 程序目录 → 作用域键。大小写与末尾分隔符不同视为同一目录（Windows 路径语义）。
    /// </summary>
    /// <param name="baseDirectory">程序所在目录；通常直接传 <c>AppContext.BaseDirectory</c>。</param>
    /// <exception cref="ArgumentException">路径为空、为空白或不合法。</exception>
    public static string ScopeFor(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var full = Path.GetFullPath(baseDirectory);
        var normalized = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalized.Length == 0)
        {
            // 只给了盘符根（"C:\"）：TrimEnd 会把分隔符吃掉，补回来保证键稳定。
            normalized = full;
        }

        // Windows 路径大小写不敏感：D:\app\PdxNext 与 d:\APP\pdxnext 必须同键。
        return Fnv1a64(normalized.ToUpperInvariant());
    }

    /// <summary>作用域锁名（真正的单实例锁）。</summary>
    public static string MutexNameFor(string baseDirectory) =>
        MutexNamePrefix + "." + ScopeFor(baseDirectory);

    /// <summary>作用域激活管道名（唤醒**同一目录**里已在运行的实例）。</summary>
    public static string PipeNameFor(string baseDirectory) =>
        PipeNamePrefix + "." + ScopeFor(baseDirectory);

    /// <summary>
    /// FNV-1a 64 位，输出定长小写十六进制。
    /// <para>
    /// 只用来把一个路径折成固定短串、避免 Mutex/管道名过长与非法字符，不承担安全职责，
    /// 所以不用 SHA-256（Core 里已有 SHA 用法，但这里要的是短且快）。
    /// </para>
    /// </summary>
    private static string Fnv1a64(string text)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= prime;
        }

        // "x16" = 定长 16 位小写十六进制，天然满足 HashLength。
        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}
