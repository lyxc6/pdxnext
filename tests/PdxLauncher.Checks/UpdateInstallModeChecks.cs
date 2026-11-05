namespace PdxLauncher.Checks;

using PdxLauncher.Core;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Updates;

/// <summary>
/// 更新器与设置页的安装形态口径自检。
/// <para>
/// 用户故障：设置页已经按 <see cref="InstallModeResolver"/> 显示真实形态，更新器却仍用
/// <c>PdxDataRoot.IsPerMachineInstall</c> 决定 <c>/ALLUSERS</c>。那个判据把 Program Files
/// 与 marker 都当成每机器证据，绿色拷贝进 Program Files 会「显示便携、更新按每机器」。
/// 这里把新判据钉死：只有「Inno 正式安装 + 每机器 + App Path 命中当前目录」才提权。
/// </para>
/// 断言分三层：纯函数矩阵、真实 Resolver 输入矩阵、源码层「判据只有一处」。
/// </summary>
public static class UpdateInstallModeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-update-mode-" + Guid.NewGuid().ToString("N"));

        try
        {
            fs.EnsureDirectory(tempRoot);
            CheckDecisionMatrix();
            CheckResolverMatrix(fs, tempRoot);
            CheckSingleSourceOfTruth();
        }
        finally
        {
            TryDelete(tempRoot);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>纯函数矩阵：任何「便携 / 残留 / 每用户」形态都不得提权。</summary>
    private static void CheckDecisionMatrix()
    {
        Console.WriteLine("[UpdateInstallModeChecks] /ALLUSERS 判定矩阵");

        var installedPerMachine = InstallMode.Installed(
            InstallScope.PerMachine, @"C:\Program Files\PdxNext", "1.2.2", UninstallRegistryView.Registry64);
        var installedPerUser = InstallMode.Installed(
            InstallScope.PerUser, @"C:\Users\me\AppData\Local\Programs\PdxNext", "1.2.2", UninstallRegistryView.Registry64);

        ChecksContext.Check(
            "更新判据: Inno perMachine 命中 → 传 /ALLUSERS",
            UpdateService.ShouldUseAllUsers(installedPerMachine));
        ChecksContext.Check(
            "更新判据: Inno perUser → 不传 /ALLUSERS",
            !UpdateService.ShouldUseAllUsers(installedPerUser));
        ChecksContext.Check(
            "更新判据: 便携 + 路径不匹配（绿色拷贝进 Program Files）→ 不传 /ALLUSERS",
            !UpdateService.ShouldUseAllUsers(InstallMode.PortableRun(
                InstallModeReason.PathMismatch,
                InstallScope.PerMachine,
                @"C:\Program Files\PdxNext",
                "1.2.2",
                UninstallRegistryView.Registry64)));
        ChecksContext.Check(
            "更新判据: 便携 + 无条目 → 不传 /ALLUSERS",
            !UpdateService.ShouldUseAllUsers(InstallMode.PortableRun(InstallModeReason.NoEntry)));
        ChecksContext.Check(
            "更新判据: 仅有 marker 的 perMachine → 不传 /ALLUSERS",
            !UpdateService.ShouldUseAllUsers(InstallMode.PortableRun(
                InstallModeReason.MarkerOnly, InstallScope.PerMachine)));
        ChecksContext.Check(
            "更新判据: 卸载残留（主程序缺失）→ 不传 /ALLUSERS",
            !UpdateService.ShouldUseAllUsers(InstallMode.PortableRun(
                InstallModeReason.ExeMissing,
                InstallScope.PerMachine,
                @"C:\Program Files\PdxNext")));
    }

    /// <summary>真实 Resolver 输入矩阵：marker + 卸载项 + 程序目录，走同一条生产判定。</summary>
    private static void CheckResolverMatrix(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[UpdateInstallModeChecks] Resolver 形态矩阵");

        // 1) Inno perMachine，App Path 与当前目录一致，主程序在。
        var perMachineDir = Path.Combine(tempRoot, "per-machine");
        fs.EnsureDirectory(perMachineDir);
        WriteMarker(fs, perMachineDir, "perMachine");
        WriteExe(fs, perMachineDir);
        var perMachine = Resolve(perMachineDir, [Entry(perMachineDir, InstallScope.PerMachine)]);
        ChecksContext.Equal("Resolver: perMachine 判为已安装", InstallModeKind.InstalledInno, perMachine.Kind);
        ChecksContext.Equal("Resolver: perMachine 范围正确", InstallScope.PerMachine, perMachine.Scope);
        ChecksContext.Check("Resolver: perMachine → 传 /ALLUSERS", UpdateService.ShouldUseAllUsers(perMachine));

        // 2) Inno perUser，App Path 一致。
        var perUserDir = Path.Combine(tempRoot, "per-user");
        fs.EnsureDirectory(perUserDir);
        WriteMarker(fs, perUserDir, "perUser");
        WriteExe(fs, perUserDir);
        var perUser = Resolve(perUserDir, [Entry(perUserDir, InstallScope.PerUser)]);
        ChecksContext.Equal("Resolver: perUser 判为已安装", InstallModeKind.InstalledInno, perUser.Kind);
        ChecksContext.Check("Resolver: perUser → 不传 /ALLUSERS", !UpdateService.ShouldUseAllUsers(perUser));

        // 3) 绿色拷贝进 Program Files：marker=perMachine，但卸载项指向别处。
        var greenDir = Path.Combine(tempRoot, "green-copy");
        fs.EnsureDirectory(greenDir);
        WriteMarker(fs, greenDir, "perMachine");
        WriteExe(fs, greenDir);
        var green = Resolve(greenDir, [Entry(@"C:\Program Files\PdxNext", InstallScope.PerMachine)]);
        ChecksContext.Equal("Resolver: 绿色拷贝判为路径不匹配", InstallModeReason.PathMismatch, green.Reason);
        ChecksContext.Check("Resolver: 绿色拷贝 → 不传 /ALLUSERS", !UpdateService.ShouldUseAllUsers(green));

        // 4) 只有 marker，没有任何卸载项。
        var markerOnlyDir = Path.Combine(tempRoot, "marker-only");
        fs.EnsureDirectory(markerOnlyDir);
        WriteMarker(fs, markerOnlyDir, "perMachine");
        WriteExe(fs, markerOnlyDir);
        var markerOnly = Resolve(markerOnlyDir, []);
        ChecksContext.Equal("Resolver: 仅 marker 判为证据不足", InstallModeReason.MarkerOnly, markerOnly.Reason);
        ChecksContext.Check("Resolver: 仅 marker → 不传 /ALLUSERS", !UpdateService.ShouldUseAllUsers(markerOnly));

        // 5) 卸载项命中但主程序缺失（卸载残留）。
        var exeMissingDir = Path.Combine(tempRoot, "exe-missing");
        fs.EnsureDirectory(exeMissingDir);
        WriteMarker(fs, exeMissingDir, "perMachine");
        var exeMissing = Resolve(exeMissingDir, [Entry(exeMissingDir, InstallScope.PerMachine)]);
        ChecksContext.Equal("Resolver: 主程序缺失判为残留", InstallModeReason.ExeMissing, exeMissing.Reason);
        ChecksContext.Check("Resolver: 主程序缺失 → 不传 /ALLUSERS", !UpdateService.ShouldUseAllUsers(exeMissing));
    }

    /// <summary>源码层：更新器只允许存在一处判据，且不得再借 IsPerMachineInstall。</summary>
    private static void CheckSingleSourceOfTruth()
    {
        Console.WriteLine("[UpdateInstallModeChecks] 更新器判据唯一性");

        var root = FindWorkspaceRoot();
        var updateService = Path.Combine(root, "src", "PdxLauncher.Core", "Updates", "UpdateService.cs");
        var updateFlow = Path.Combine(root, "src", "PdxLauncher.App", "Services", "UpdateFlow.cs");
        var app = Path.Combine(root, "src", "PdxLauncher.App", "App.xaml.cs");

        if (!ChecksContext.Check("更新判据: UpdateService.cs 存在", File.Exists(updateService), updateService) ||
            !ChecksContext.Check("更新判据: UpdateFlow.cs 存在", File.Exists(updateFlow), updateFlow))
        {
            return;
        }

        var service = File.ReadAllText(updateService);
        var flow = File.ReadAllText(updateFlow);

        ChecksContext.Check(
            "更新判据: UpdateService 用 InstallModeResolver 判定",
            service.Contains("InstallModeResolver.Resolve(", StringComparison.Ordinal));
        ChecksContext.Check(
            "更新判据: UpdateService 不再调用 IsPerMachineInstall",
            !service.Contains("IsPerMachineInstall", StringComparison.Ordinal),
            "旧判据会把 Program Files 里的绿色拷贝当成每机器安装");
        ChecksContext.Check(
            "更新判据: LaunchInstaller 的 /ALLUSERS 由 ShouldUseAllUsers 决定",
            service.Contains("WillUseAllUsers", StringComparison.Ordinal) &&
            service.Contains("ShouldUseAllUsers(", StringComparison.Ordinal));
        ChecksContext.Check(
            "更新判据: UpdateFlow 的提权提示与启动参数同源",
            flow.Contains("WillUseAllUsers", StringComparison.Ordinal) &&
            !flow.Contains("IsPerMachineInstall", StringComparison.Ordinal));
        ChecksContext.Check(
            "更新判据: App 注入注册表卸载项来源",
            File.Exists(app) &&
            File.ReadAllText(app).Contains("uninstallEntries: new RegistryUninstallEntrySource(", StringComparison.Ordinal));
    }

    private static InstallMode Resolve(string directory, IReadOnlyList<UninstallEntry> entries) =>
        InstallModeResolver.Resolve(directory, PdxDataRoot.ReadInstallMode(directory), entries);

    private static UninstallEntry Entry(string appPath, InstallScope scope) =>
        new(scope, appPath, "1.2.2", appPath, UninstallRegistryView.Registry64);

    private static void WriteMarker(IFileSystem fs, string directory, string marker) =>
        fs.WriteAllTextAtomic(Path.Combine(directory, PdxDataRoot.InstallModeFileName), marker);

    private static void WriteExe(IFileSystem fs, string directory) =>
        fs.WriteAllTextAtomic(Path.Combine(directory, PdxIdentity.AppExecutableName), "stub");

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录清理失败不影响断言结论。
        }
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
