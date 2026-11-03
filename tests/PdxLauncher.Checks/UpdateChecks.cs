namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Updates;

/// <summary>
/// 更新链路中不需要网络的纯逻辑自检：版本比较、清单校验、默认清单地址与安装模式标记。
/// 网络下载与 UAC 启动属于实机验证，不放进这个进程。
/// </summary>
public static class UpdateChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-update-" + Guid.NewGuid().ToString("N"));

        try
        {
            fs.EnsureDirectory(tempRoot);
            CheckVersions();
            CheckManifest();
            CheckCurrentVersion();
            CheckInstallModeMarker(fs, tempRoot);
        }
        finally
        {
            TryDelete(tempRoot);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckVersions()
    {
        Console.WriteLine("[UpdateChecks] 版本解析与比较");

        CheckParse("1.2.3", "1.2.3");
        CheckParse("v1.2.3", "1.2.3");
        CheckParse("1.2", "1.2.0");
        CheckParse("1", "1.0.0");
        CheckParse("1.2.3.4", null);
        CheckParse("1.2.x", null);
        CheckParse("", null);
        CheckParse("-1.2.3", null);

        var withMetadata = UpdateVersion.TryParse("1.2.3+ad4f712", out var metadata);
        ChecksContext.Check("忽略 +commit 元数据", withMetadata);
        ChecksContext.Equal("元数据不参与比较", "1.2.3", metadata.ToString());

        var withPreview = UpdateVersion.TryParse("1.2.3-preview.4", out var preview);
        ChecksContext.Check("忽略 -preview 后缀", withPreview);
        ChecksContext.Equal("预发布后缀被截断", "1.2.3", preview.ToString());

        ChecksContext.Check("1.2.3 > 1.2.2", UpdateVersion.Parse("1.2.3") > UpdateVersion.Parse("1.2.2"));
        ChecksContext.Check("1.10.0 > 1.9.9", UpdateVersion.Parse("1.10.0") > UpdateVersion.Parse("1.9.9"));
        ChecksContext.Check("1.0.0 < 2.0.0", UpdateVersion.Parse("1.0.0") < UpdateVersion.Parse("2.0.0"));
        ChecksContext.Equal("相等版本比较为 0", 0, UpdateVersion.Parse("1.2.3").CompareTo(UpdateVersion.Parse("v1.2.3")));
    }

    private static void CheckManifest()
    {
        Console.WriteLine("[UpdateChecks] 更新清单校验");

        var sha = new string('a', 64);
        var valid = BuildJson("1.2.3", "https://github.com/lyxc6/pdxnext/releases/download/v1.2.3/PdxNext-Setup-1.2.3.exe", sha, 123456);
        ChecksContext.Check("合法清单通过", UpdateManifest.TryParse(valid, out var manifest, out var error), error);
        ChecksContext.Check("合法清单不是 null", manifest is not null);
        if (manifest is not null)
        {
            ChecksContext.Equal("版本被规范化", "1.2.3", manifest.Version);
            ChecksContext.Equal("摘要转小写", sha, manifest.Sha256);
            ChecksContext.Equal("安装包大小保留", 123456L, manifest.Size);
            ChecksContext.Equal("安装包 URI 可构造", "github.com", manifest.SetupUri.Host);
        }

        Reject("拒绝 http", BuildJson("1.2.3", "http://github.com/a/PdxNext-Setup.exe", sha, 1));
        Reject("拒绝非白名单主机", BuildJson("1.2.3", "https://evil.example.com/PdxNext-Setup.exe", sha, 1));
        Reject("拒绝非 exe", BuildJson("1.2.3", "https://github.com/a/PdxNext-Setup.zip", sha, 1));
        Reject("拒绝短摘要", BuildJson("1.2.3", "https://github.com/a/PdxNext-Setup.exe", "abc", 1));
        Reject("拒绝非十六进制摘要", BuildJson("1.2.3", "https://github.com/a/PdxNext-Setup.exe", new string('z', 64), 1));
        Reject("拒绝零大小", BuildJson("1.2.3", "https://github.com/a/PdxNext-Setup.exe", sha, 0));
        Reject("拒绝无效版本", BuildJson("1.2.x", "https://github.com/a/PdxNext-Setup.exe", sha, 1));
        Reject("拒绝未来 schema", BuildJson("1.2.3", "https://github.com/a/PdxNext-Setup.exe", sha, 1, schemaVersion: 2));

        var notesOverHttp = valid[..^1] + ",\"releaseNotesUrl\":\"http://example.com/notes\"}";
        Reject("拒绝 http 发布说明", notesOverHttp);

        var notesHttps = valid[..^1] + ",\"releaseNotesUrl\":\"https://github.com/lyxc6/pdxnext/releases/tag/v1.2.3\"}";
        ChecksContext.Check("接受 https 发布说明", UpdateManifest.TryParse(notesHttps, out _, out var notesError), notesError);

        ChecksContext.Check("空清单被拒绝", !UpdateManifest.TryParse(null, out _, out _));
        ChecksContext.Check("坏 JSON 被拒绝", !UpdateManifest.TryParse("{", out _, out _));

        static void Reject(string name, string json) =>
            ChecksContext.Check(name, !UpdateManifest.TryParse(json, out _, out _));
    }

    private static void CheckCurrentVersion()
    {
        Console.WriteLine("[UpdateChecks] 当前版本读取");
        var text = UpdateService.ReadCurrentVersion();
        ChecksContext.Check("当前版本非空", !string.IsNullOrWhiteSpace(text));
        ChecksContext.Check("当前版本可解析", UpdateVersion.TryParse(text, out _), text);
    }

    private static void CheckInstallModeMarker(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[UpdateChecks] 安装模式标记");

        var perMachine = Path.Combine(tempRoot, "per-machine");
        fs.EnsureDirectory(perMachine);
        fs.WriteAllTextAtomic(Path.Combine(perMachine, PdxDataRoot.InstallModeFileName), "perMachine");
        ChecksContext.Check("marker=perMachine 判为每机器", PdxDataRoot.IsPerMachineInstall(perMachine));

        var perUser = Path.Combine(tempRoot, "per-user");
        fs.EnsureDirectory(perUser);
        fs.WriteAllTextAtomic(Path.Combine(perUser, PdxDataRoot.InstallModeFileName), "perUser");
        ChecksContext.Check("marker=perUser 判为每用户", !PdxDataRoot.IsPerMachineInstall(perUser));

        var portable = Path.Combine(tempRoot, "portable");
        fs.EnsureDirectory(portable);
        fs.WriteAllTextAtomic(Path.Combine(portable, PdxDataRoot.InstallModeFileName), "portable");
        ChecksContext.Check("marker=portable 判为每用户", !PdxDataRoot.IsPerMachineInstall(portable));

        var missing = Path.Combine(tempRoot, "missing-marker");
        fs.EnsureDirectory(missing);
        ChecksContext.Check("临时可写目录无 marker 判为每用户", !PdxDataRoot.IsPerMachineInstall(missing));
    }

    private static void CheckParse(string text, string? expected)
    {
        var ok = UpdateVersion.TryParse(text, out var version);
        if (expected is null)
        {
            ChecksContext.Check("拒绝版本 " + text, !ok);
            return;
        }

        ChecksContext.Check("解析版本 " + text, ok);
        if (ok)
        {
            ChecksContext.Equal("规范化版本 " + text, expected, version.ToString());
        }
    }

    private static string BuildJson(string version, string setupUrl, string sha, long size, int schemaVersion = 1) =>
        "{\"schemaVersion\":" + schemaVersion +
        ",\"version\":\"" + version +
        "\",\"setupUrl\":\"" + setupUrl +
        "\",\"sha256\":\"" + sha +
        "\",\"size\":" + size + "}";

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
            Console.WriteLine("[UpdateChecks] 临时目录未删除: " + ex.Message);
        }
    }
}
