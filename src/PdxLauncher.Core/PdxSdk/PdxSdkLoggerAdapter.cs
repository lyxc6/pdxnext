namespace PdxLauncher.Core.PdxSdk;

using PdxLauncher.Core.Abstractions;
using PdxLogger = PDX.SDK.Contracts.Logging.ILogger;
using PdxLogLevel = PDX.SDK.Contracts.Enums.LogLevel;
using PdxFlowData = PDX.SDK.Contracts.Internal.FlowData;

/// <summary>
/// 把官方 <c>PDX.SDK</c> 的日志接口接到本项目的 <see cref="IAppLogger"/>。
/// <para>
/// SDK 的 <c>LogLevel</c> 是五档（L0_Info…L4_Fatal）；本项目只区分 Info/Warn/Error，
/// 所以 L2 及以下归 Warn，L3/L4 归 Error。SDK 会把 token 写进 L1_Debug 日志，
/// 这里统一降级、绝不回显到我们的日志里。
/// </para>
/// </summary>
public sealed class PdxSdkLoggerAdapter : PdxLogger
{
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    /// <param name="logger">本项目日志。</param>
    public PdxSdkLoggerAdapter(IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public void Log(string msg, PdxLogLevel logLevel = PdxLogLevel.L1_Debug, PdxFlowData? flowData = null)
    {
        if (string.IsNullOrWhiteSpace(msg))
        {
            return;
        }

        switch (logLevel)
        {
            case PdxLogLevel.L3_Error:
            case PdxLogLevel.L4_Fatal:
                _logger.Error("[pdx-sdk] " + msg);
                break;
            case PdxLogLevel.L2_Warning:
                _logger.Warn("[pdx-sdk] " + msg);
                break;
            default:
                _logger.Info("[pdx-sdk] " + msg);
                break;
        }
    }
}
