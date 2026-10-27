namespace PdxLauncher.Core.Infrastructure;

/// <summary>真实系统时钟。</summary>
public sealed class SystemClock : Abstractions.IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
