namespace PdxLauncher.Core.Contracts;

/// <summary>启动请求。CustomExePath 为空表示用 launcher-settings 里的 exePath。</summary>
public sealed record LaunchRequest(
    string? CustomExePath,
    IReadOnlyList<string>? CustomExeArgs,
    bool ContinueLastSave)
{
    /// <summary>默认启动：标准可执行文件 + 标准参数。</summary>
    public static LaunchRequest Default { get; } = new(null, null, false);
}
