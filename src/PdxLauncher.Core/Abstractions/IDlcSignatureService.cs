using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>dlc_signature 计算与写入（默认不写，仅手动触发）。</summary>
public interface IDlcSignatureService
{
    Task<string> ComputeAsync(
        GameInstallation game,
        IReadOnlyList<DlcInfo> dlcs,
        CancellationToken cancellationToken);

    Task WriteAsync(
        GameInstallation game,
        IReadOnlyList<DlcInfo> dlcs,
        CancellationToken cancellationToken);
}
