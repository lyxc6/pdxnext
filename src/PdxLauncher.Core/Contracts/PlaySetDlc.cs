namespace PdxLauncher.Core.Contracts;

/// <summary>playsets_dlcs 表的一行；DlcId 为 DLC 的 pops_id。</summary>
public sealed record PlaySetDlc(string DlcId, bool Enabled);
