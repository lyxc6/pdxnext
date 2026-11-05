namespace PdxLauncher.Checks;

using PdxLauncher.Core;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 安装模式判定自检。全部用假的 <see cref="UninstallEntry"/> 列表 + 临时目录里的桩 exe，
/// 不读真注册表；断言的正是「安装包装的却显示便携」这个用户可见 bug 的回归场景。
/// </summary>
public static class InstallModeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-install-mode-" + Guid.NewGuid().ToString("N"));

        try
        {
            fs.EnsureDirectory(tempRoot);
            CheckRealWorldRegression(fs, tempRoot);
            CheckScopes(fs, tempRoot);
            CheckPathNormalization(fs, tempRoot);
            CheckResidue(fs, tempRoot);
            CheckMarkerOnly(fs, tempRoot);
            CheckDegenerateInputs(fs, tempRoot);
        }
        finally
        {
            TryDelete(tempRoot);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>
    /// 本机实测场景：用户把程序装在 D:\app\PdxNext（不在 %LOCALAPPDATA%\Programs 下），
    /// HKCU 卸载项的 App Path 与安装目录一致、exe 在。旧实现只比 LOCALAPPDATA，必然判成便携。
    /// 这里用临时目录复刻同形路径，保证任何机器上都能回归；D 盘真目录存在时再补跑一次。
    /// </summary>
    private static void CheckRealWorldRegression(IFileSystem fs, string root)
    {
        Console.WriteLine("[InstallModeChecks] 本机真实场景回归：自选目录安装");

        var installDir = CreateInstallDir(fs, root, Path.Combine("app", "PdxNext"), withExe: true);
        ExpectInstalled(
            "HKCU 条目 AppPath 一致 + exe 存在",
            InstallModeResolver.Resolve(installDir, null, [Entry(installDir, InstallScope.PerUser)]),
            InstallScope.PerUser);

        var realDir = @"D:\app\PdxNext";
        if (fs.DirectoryExists(realDir) &&
            fs.FileExists(Path.Combine(realDir, PdxIdentity.AppExecutableName)))
        {
            ExpectInstalled(
                "本机 D:\\app\\PdxNext 实目录",
                InstallModeResolver.Resolve(realDir, "perUser", [Entry(realDir, InstallScope.PerUser)]),
                InstallScope.PerUser);
        }
    }

    private static void CheckScopes(IFileSystem fs, string root)
    {
        Console.WriteLine("[InstallModeChecks] Scope 与注册表视图");

        var machineDir = CreateInstallDir(fs, root, Path.Combine("machine", "PdxNext"), withExe: true);
        ExpectInstalled(
            "HKLM 条目",
            InstallModeResolver.Resolve(machineDir, "perMachine", [Entry(machineDir, InstallScope.PerMachine)]),
            InstallScope.PerMachine);

        var viewDir = CreateInstallDir(fs, root, Path.Combine("view32", "PdxNext"), withExe: true);
        ExpectInstalled(
            "32 位视图（WOW6432Node）条目",
            InstallModeResolver.Resolve(
                viewDir,
                null,
                [Entry(viewDir, InstallScope.PerUser, UninstallRegistryView.Registry32)]),
            InstallScope.PerUser,
            UninstallRegistryView.Registry32);

        var bothDir = CreateInstallDir(fs, root, Path.Combine("both", "PdxNext"), withExe: true);
        UninstallEntry[] both = [Entry(bothDir, InstallScope.PerUser), Entry(bothDir, InstallScope.PerMachine)];
        ExpectInstalled(
            "双条目 + marker=perMachine 取 HKLM",
            InstallModeResolver.Resolve(bothDir, "perMachine", both),
            InstallScope.PerMachine);
        ExpectInstalled(
            "双条目无 marker 默认 PerUser",
            InstallModeResolver.Resolve(bothDir, null, both),
            InstallScope.PerUser);
    }

    private static void CheckPathNormalization(IFileSystem fs, string root)
    {
        Console.WriteLine("[InstallModeChecks] 路径归一化");

        var installDir = CreateInstallDir(fs, root, Path.Combine("norm", "PdxNext"), withExe: true);
        ExpectInstalled(
            "大小写不同",
            InstallModeResolver.Resolve(installDir, null, [Entry(installDir.ToUpperInvariant())]),
            InstallScope.PerUser);
        ExpectInstalled(
            "尾斜杠",
            InstallModeResolver.Resolve(installDir, null, [Entry(installDir + Path.DirectorySeparatorChar)]),
            InstallScope.PerUser);
        ExpectInstalled(
            ".. 相对段",
            InstallModeResolver.Resolve(
                installDir,
                null,
                [Entry(Path.Combine(installDir, "..", "PdxNext"))]),
            InstallScope.PerUser);
    }

    private static void CheckResidue(IFileSystem fs, string root)
    {
        Console.WriteLine("[InstallModeChecks] 卸载残留不算已安装");

        var mismatchDir = CreateInstallDir(fs, root, Path.Combine("mismatch", "PdxNext"), withExe: true);
        var elsewhere = Path.Combine(root, "elsewhere", "PdxNext");
        ExpectPortable(
            "条目路径不匹配",
            InstallModeResolver.Resolve(mismatchDir, "perUser", [Entry(elsewhere)]),
            InstallModeReason.PathMismatch);

        var residueDir = CreateInstallDir(fs, root, Path.Combine("residue", "PdxNext"), withExe: false);
        var residue = InstallModeResolver.Resolve(
            residueDir,
            "perMachine",
            [Entry(residueDir, InstallScope.PerMachine)]);
        ExpectPortable("条目在但 exe 缺失", residue, InstallModeReason.ExeMissing);
        ChecksContext.Check(
            "卸载残留仍记录 Scope（诊断用，不改变 IsInstalled）",
            residue.Scope == InstallScope.PerMachine,
            residue.Scope?.ToString() ?? "null");
    }

    private static void CheckMarkerOnly(IFileSystem fs, string root)
    {
        Console.WriteLine("[InstallModeChecks] marker 只做佐证");

        var markerDir = CreateInstallDir(fs, root, Path.Combine("marker", "PdxNext"), withExe: true);
        ExpectPortable(
            "无条目 + marker=perUser（不许单独成立）",
            InstallModeResolver.Resolve(markerDir, "perUser", []),
            InstallModeReason.MarkerOnly);
        ExpectPortable(
            "无条目 + marker=perMachine",
            InstallModeResolver.Resolve(markerDir, "  perMachine  ", []),
            InstallModeReason.MarkerOnly);

        var noEntryDir = CreateInstallDir(fs, root, Path.Combine("noentry", "PdxNext"), withExe: true);
        ExpectPortable(
            "无条目无 marker",
            InstallModeResolver.Resolve(noEntryDir, null, []),
            InstallModeReason.NoEntry);
        ExpectPortable(
            "空 marker 视作无 marker",
            InstallModeResolver.Resolve(noEntryDir, "   ", []),
            InstallModeReason.NoEntry);
    }

    private static void CheckDegenerateInputs(IFileSystem fs, string root)
    {
        Console.WriteLine("[InstallModeChecks] 退化输入不抛异常");

        var installDir = CreateInstallDir(fs, root, Path.Combine("degenerate", "PdxNext"), withExe: true);
        ExpectPortable(
            "空程序目录",
            InstallModeResolver.Resolve(string.Empty, null, []),
            InstallModeReason.NoEntry);
        ExpectPortable(
            "空程序目录 + 条目 + marker",
            InstallModeResolver.Resolve(string.Empty, "perUser", [Entry(string.Empty)]),
            InstallModeReason.NoEntry);
        ExpectPortable(
            "空条目列表",
            InstallModeResolver.Resolve(installDir, null, []),
            InstallModeReason.NoEntry);
        ExpectPortable(
            "条目 AppPath 为空/空白",
            InstallModeResolver.Resolve(installDir, null, [Entry(null), Entry("   ")]),
            InstallModeReason.PathMismatch);
        ExpectPortable(
            "非法程序目录",
            InstallModeResolver.Resolve("bad|dir", null, []),
            InstallModeReason.NoEntry);
    }

    private static string CreateInstallDir(IFileSystem fs, string root, string relative, bool withExe)
    {
        var directory = Path.Combine(root, relative);
        fs.EnsureDirectory(directory);
        if (withExe)
        {
            fs.WriteAllTextAtomic(Path.Combine(directory, PdxIdentity.AppExecutableName), "stub");
        }

        return directory;
    }

    private static UninstallEntry Entry(
        string? appPath,
        InstallScope scope = InstallScope.PerUser,
        UninstallRegistryView view = UninstallRegistryView.Registry64) =>
        new(scope, appPath, "1.2.2", appPath, view);

    private static void ExpectInstalled(
        string label,
        InstallMode mode,
        InstallScope scope,
        UninstallRegistryView view = UninstallRegistryView.Registry64)
    {
        ChecksContext.Check(
            label + " → 已安装",
            mode.Kind == InstallModeKind.InstalledInno && mode.IsInstalled,
            Describe(mode));
        ChecksContext.Check(label + " → scope 为 " + scope, mode.Scope == scope, Describe(mode));
        ChecksContext.Check(
            label + " → 原因 MatchedInstallDir",
            mode.Reason == InstallModeReason.MatchedInstallDir,
            mode.Reason.ToString());
        ChecksContext.Check(label + " → 注册表视图为 " + view, mode.RegistryView == view, mode.RegistryView?.ToString() ?? "null");
        ChecksContext.Check(label + " → 安装目录非空", !string.IsNullOrWhiteSpace(mode.InstallDirectory), mode.InstallDirectory ?? "null");
    }

    private static void ExpectPortable(string label, InstallMode mode, InstallModeReason reason)
    {
        ChecksContext.Check(
            label + " → 便携运行",
            mode.Kind == InstallModeKind.PortableRun && !mode.IsInstalled,
            Describe(mode));
        ChecksContext.Check(label + " → 原因 " + reason, mode.Reason == reason, mode.Reason.ToString());
    }

    private static string Describe(InstallMode mode) =>
        mode.Kind + "/" + mode.Reason + "/scope=" + (mode.Scope?.ToString() ?? "null") +
        "/dir=" + (mode.InstallDirectory ?? "null");

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("[InstallModeChecks] 临时目录未删除: " + ex.Message);
        }
    }
}
