namespace PdxLauncher.Core.Contracts;

/// <summary>launcher-v2.sqlite 中 playsets 表的一行。</summary>
public sealed record PlaySet(Guid Id, string Name, bool IsActive);
