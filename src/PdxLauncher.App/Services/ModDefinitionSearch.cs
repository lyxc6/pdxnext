namespace PdxLauncher.App.Services;

using System;
using System.Collections.Generic;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 本地文档全文检索（第 3 部分）：读已导入的脚本文档，按行建立索引，再做大小写不敏感的
/// 子串检索（支持中文）。只依赖 System.* + <see cref="IFileSystem"/>，可被自检工程原样链接。
/// <para>全程全本地：不构造 URL、不发 HTTP。</para>
/// </summary>
public static class ModDefinitionSearch
{
    /// <summary>建立索引时单个文件读取的字节上限（超过就跳过，避免大文件拖慢页面）。</summary>
    public const int DefaultMaxBytes = 2 * 1024 * 1024;

    /// <summary>建立索引时纳入的文件数上限。</summary>
    public const int DefaultMaxFiles = 2000;

    /// <summary>索引中的一行：文件路径、1 起的行号、该行文本。</summary>
    public sealed record IndexedLine(string Path, int LineNumber, string Text);

    /// <summary>一条命中：文件路径、1 起的行号、命中行文本。</summary>
    public sealed record SearchHit(string Path, int LineNumber, string LineText);

    /// <summary>
    /// 从给定文件列表建立逐行索引。读取失败或超过 <paramref name="maxBytes"/> 的文件跳过，
    /// 最多取 <paramref name="maxFiles"/> 个文件。行尾（\r）会被去掉，空行也纳入索引。
    /// </summary>
    public static IReadOnlyList<IndexedLine> BuildIndex(
        IEnumerable<string> files,
        IFileSystem fileSystem,
        int maxBytes = DefaultMaxBytes,
        int maxFiles = DefaultMaxFiles)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var result = new List<IndexedLine>();
        var taken = 0;
        foreach (var path in files)
        {
            if (taken >= maxFiles)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(path) || !fileSystem.FileExists(path))
            {
                continue;
            }

            long size;
            try
            {
                size = fileSystem.GetFileSize(path);
            }
            catch
            {
                continue;
            }

            if (size > maxBytes)
            {
                continue;
            }

            string text;
            try
            {
                text = fileSystem.ReadAllText(path);
            }
            catch
            {
                continue;
            }

            taken++;
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                result.Add(new IndexedLine(path, i + 1, line));
            }
        }

        return result;
    }

    /// <summary>
    /// 在索引里检索 query（大小写不敏感、支持中文）。空词返回空列表。
    /// 结果按索引顺序（文件顺序 + 行号升序）。
    /// </summary>
    public static IReadOnlyList<SearchHit> Search(IReadOnlyList<IndexedLine> index, string? query)
    {
        if (index is null || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchHit>();
        }

        var needle = query.Trim();
        var hits = new List<SearchHit>();
        foreach (var line in index)
        {
            if (line.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hits.Add(new SearchHit(line.Path, line.LineNumber, line.Text));
            }
        }

        return hits;
    }
}
