namespace PdxLauncher.Core.Abstractions;

/// <summary>时间抽象，便于自检注入固定时间。</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
