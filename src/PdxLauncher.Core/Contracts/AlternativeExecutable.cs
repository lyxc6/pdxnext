namespace PdxLauncher.Core.Contracts;

/// <summary>launcher-settings.json 里的 alternativeExecutables 条目（如调试模式启动）。</summary>
public sealed record AlternativeExecutable(
    string ExePath,
    IReadOnlyList<string> Args,
    string DisplayLabel);
