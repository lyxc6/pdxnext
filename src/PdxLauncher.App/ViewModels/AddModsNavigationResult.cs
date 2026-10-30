namespace PdxLauncher.App.ViewModels;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 「添加模组」子页的出参：用户在子页里勾选的 mod。
/// 真正写库由播放集页完成（它才有当前播放集的完整上下文与自动保存逻辑）。
/// </summary>
/// <param name="PlaySetId">这些 mod 要加入的播放集 Id。</param>
/// <param name="Entries">用户勾选的 mod 库条目。</param>
public sealed record AddModsNavigationResult(Guid PlaySetId, IReadOnlyList<ModLibraryEntry> Entries);
