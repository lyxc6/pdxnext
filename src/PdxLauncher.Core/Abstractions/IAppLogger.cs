namespace PdxLauncher.Core.Abstractions;

/// <summary>日志的唯一入口。</summary>
public interface IAppLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
