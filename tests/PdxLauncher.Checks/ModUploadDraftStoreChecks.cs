namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Steam;

/// <summary>
/// 上传草稿文件存储自检：MCP 进程和启动器进程靠这一份 JSON 交接，路径哈希、
/// 大小写归一、坏 JSON 降级和删除语义错一个，AI 写的草稿就会“看起来保存了但界面读不到”。
/// </summary>
public static class ModUploadDraftStoreChecks
{
    /// <summary>运行本组检查。</summary>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;
        var root = PlaysetChecks.CreateTempRoot(fileSystem, "mcp-draft");
        try
        {
            Console.WriteLine("[ModUploadDraftStoreChecks] 保存 / 读取 / 删除");
            var store = new ModUploadDraftStore(fileSystem, root);
            var modPath = fileSystem.Combine(root, "mods", "MyMod") + Path.DirectorySeparatorChar;
            var updatedAt = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
            var draft = new ModUploadDraft(
                "victoria3",
                modPath,
                "测试标题",
                "测试描述",
                ["Gameplay", "Balance"],
                SteamWorkshopVisibility.FriendsOnly,
                "更新说明",
                [1, 5],
                updatedAt);

            ChecksContext.Check("保存草稿", store.Save(draft));
            ChecksContext.Check(
                "草稿落在 mcp\\upload-drafts 下",
                store.PathFor("victoria3", modPath).StartsWith(
                    fileSystem.Combine(root, "mcp", "upload-drafts"),
                    StringComparison.OrdinalIgnoreCase));

            var loaded = store.TryGet("victoria3", modPath);
            ChecksContext.Check("读回草稿", loaded is not null);
            if (loaded is not null)
            {
                ChecksContext.Equal("游戏 id", "victoria3", loaded.GameId);
                ChecksContext.Equal("标题", "测试标题", loaded.Title);
                ChecksContext.Equal("描述", "测试描述", loaded.Description);
                ChecksContext.Equal("标签", "Gameplay,Balance", string.Join(",", loaded.Tags));
                ChecksContext.Equal("可见性", SteamWorkshopVisibility.FriendsOnly, loaded.Visibility);
                ChecksContext.Equal("更新说明", "更新说明", loaded.ChangeNote);
                ChecksContext.Equal("内容描述词", "1,5", string.Join(",", loaded.Descriptors));
                ChecksContext.Equal("更新时间", updatedAt, loaded.UpdatedAt);
            }

            // 同一个 mod：大小写不同、带不带尾分隔符都必须命中同一条草稿。
            var lowerCasePath = fileSystem.Combine(root, "mods", "mymod");
            ChecksContext.Check("路径大小写归一", store.TryGet("victoria3", lowerCasePath) is not null);

            // gameId 别名在保存和读取两侧都折成正名。
            ChecksContext.Check(
                "gameId 别名归一",
                store.Save(draft with { GameId = "cities_v2" })
                && store.TryGet("cities_skylines", modPath)?.GameId == "cities_skylines");

            Console.WriteLine("[ModUploadDraftStoreChecks] 坏数据与删除");
            fileSystem.WriteAllTextAtomic(store.PathFor("victoria3", modPath), "{ this is not json");
            ChecksContext.Check("坏 JSON 返回 null", store.TryGet("victoria3", modPath) is null);

            ChecksContext.Check("删除草稿", store.Delete("victoria3", modPath));
            ChecksContext.Check("删除后读不到", store.TryGet("victoria3", modPath) is null);
            ChecksContext.Check("重复删除返回 false", !store.Delete("victoria3", modPath));

            Console.WriteLine("[ModUploadDraftStoreChecks] 路径隔离");
            ChecksContext.Check(
                "不同 mod 不串草稿",
                store.Save(draft)
                && store.TryGet("victoria3", fileSystem.Combine(root, "mods", "OtherMod")) is null);
        }
        finally
        {
            PlaysetChecks.TryDelete(fileSystem, root);
        }

        return ChecksContext.Failures - before;
    }
}
