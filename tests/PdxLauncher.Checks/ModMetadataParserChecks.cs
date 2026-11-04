namespace PdxLauncher.Checks;

using System.Text;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// V3 / EU5 的 <c>.metadata/metadata.json</c> 解析护栏：id、relationships 里只认 dependency、
/// 缺 id 的关系项与坏 JSON 都退化成空描述而不抛异常、BOM 由调用方剥掉后仍能解析。
/// <para>纯内存用例，不读写磁盘。</para>
/// </summary>
public static class ModMetadataParserChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckIdAndDependencies();
        CheckRelationFiltering();
        CheckDeduplicates();
        CheckBrokenInputs();
        CheckBomHandling();
        return ChecksContext.Failures - before;
    }

    private static void CheckIdAndDependencies()
    {
        const string Json = """
            {
              "name": "[1.13] Tech & Res",
              "id": "tech.res",
              "version": "1.6",
              "relationships": [
                { "rel_type": "dependency", "id": "tech.res.core", "display_name": "Core", "resource_type": "mod" }
              ]
            }
            """;

        var descriptor = ModMetadataParser.Parse(Json);
        ChecksContext.Equal("元数据 id", "tech.res", descriptor.Id);
        ChecksContext.Equal("依赖 id 数量", 1, descriptor.DependencyIds.Count);
        ChecksContext.Equal("依赖 id", "tech.res.core", descriptor.DependencyIds[0]);
    }

    private static void CheckRelationFiltering()
    {
        const string Json = """
            {
              "id": "local.pack",
              "relationships": [
                { "rel_type": "conflict", "id": "someone.else" },
                { "rel_type": "load_after", "id": "someone.else" },
                { "rel_type": "Dependency", "id": "Upper.Case" }
              ]
            }
            """;

        var descriptor = ModMetadataParser.Parse(Json);
        ChecksContext.Equal("只收 dependency（且忽略大小写）", 1, descriptor.DependencyIds.Count);
        if (descriptor.DependencyIds.Count == 1)
        {
            ChecksContext.Equal("dependency 目标 id", "Upper.Case", descriptor.DependencyIds[0]);
        }
    }

    private static void CheckDeduplicates()
    {
        const string Json = """
            {
              "id": "a",
              "relationships": [
                { "rel_type": "dependency", "id": "b" },
                { "rel_type": "dependency", "id": "B" },
                { "rel_type": "dependency", "id": "c" }
              ]
            }
            """;

        var descriptor = ModMetadataParser.Parse(Json);
        ChecksContext.Equal("重复依赖按忽略大小写去重", 2, descriptor.DependencyIds.Count);
    }

    private static void CheckBrokenInputs()
    {
        ChecksContext.Equal("空字符串 → 空描述", null, ModMetadataParser.Parse(string.Empty).Id);
        ChecksContext.Equal("坏 JSON → 空描述", 0, ModMetadataParser.Parse("{ \"id\": ").DependencyIds.Count);
        ChecksContext.Equal("根是数组 → 空描述", null, ModMetadataParser.Parse("[]").Id);
        ChecksContext.Equal("没有 relationships → 只有 id", "only.id", ModMetadataParser.Parse("{\"id\":\"only.id\"}").Id);

        var noId = ModMetadataParser.Parse("{\"relationships\":[{\"rel_type\":\"dependency\",\"id\":\"x\"}]}");
        ChecksContext.Equal("缺 id → 自身 id 为 null", null, noId.Id);
        ChecksContext.Equal("缺 id 不影响依赖解析", 1, noId.DependencyIds.Count);

        var numericId = ModMetadataParser.Parse("{\"id\":42,\"relationships\":[{\"rel_type\":\"dependency\",\"id\":7}]}");
        ChecksContext.Equal("非字符串 id 忽略", null, numericId.Id);
        ChecksContext.Equal("非字符串目标 id 忽略", 0, numericId.DependencyIds.Count);
    }

    private static void CheckBomHandling()
    {
        var json = Encoding.UTF8.GetBytes("{\"id\":\"eu5.mod\",\"relationships\":[{\"rel_type\":\"dependency\",\"id\":\"core\"}]}");
        var withBom = new byte[json.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        json.CopyTo(withBom, 3);

        var descriptor = ModMetadataParser.Parse(JsonBytes.WithoutBom(withBom));
        ChecksContext.Equal("BOM 剥掉后能解析 id", "eu5.mod", descriptor.Id);
        ChecksContext.Equal("BOM 剥掉后能解析依赖", 1, descriptor.DependencyIds.Count);
    }
}
