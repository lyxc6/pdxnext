namespace PdxLauncher.Checks;

using System.Xml.Linq;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 发版版本自检：version.props 的三个版本字段必须一致，CHANGELOG 必须有对应条目。
/// 在 CI 和本地自检里都跑，防止「改了代码忘了改版本」或「改了版本忘了写 CHANGELOG」。
/// 已安装程序的版本由 tools/verify-installed-version.ps1 单独校验（CI 没有安装实例）。
/// </summary>
public static class ReleaseVersionChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        Console.WriteLine("[ReleaseVersionChecks] version.props 与 CHANGELOG");

        var root = FindRepositoryRoot();
        if (root is null)
        {
            ChecksContext.Check("找到仓库根目录（version.props）", false);
            return 1;
        }

        var versionFile = Path.Combine(root, "version.props");
        var directoryBuild = Path.Combine(root, "Directory.Build.props");
        var changelogFile = Path.Combine(root, "CHANGELOG.md");

        ChecksContext.Check("version.props 存在", File.Exists(versionFile));
        ChecksContext.Check("Directory.Build.props 导入 version.props",
            File.Exists(directoryBuild) &&
            File.ReadAllText(directoryBuild).Contains("version.props", StringComparison.Ordinal));

        var document = XDocument.Load(versionFile);
        var group = document.Root?.Element("PropertyGroup");
        var version = group?.Element("Version")?.Value.Trim();
        var fileVersion = group?.Element("FileVersion")?.Value.Trim();
        var assemblyVersion = group?.Element("AssemblyVersion")?.Value.Trim();

        ChecksContext.Check("Version 是三段数字", IsThreePartVersion(version), version);
        ChecksContext.Equal("FileVersion 与 Version 对齐", version + ".0", fileVersion);
        ChecksContext.Equal("AssemblyVersion 与 Version 对齐", version + ".0", assemblyVersion);

        if (!string.IsNullOrWhiteSpace(version) && File.Exists(changelogFile))
        {
            var changelog = File.ReadAllText(changelogFile);
            ChecksContext.Check(
                "CHANGELOG 有 " + version + " 的条目",
                changelog.Contains("## [" + version + "]", StringComparison.Ordinal));
        }
        else
        {
            ChecksContext.Check("CHANGELOG.md 存在", false);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static bool IsThreePartVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.');
        return parts.Length == 3 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "version.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
