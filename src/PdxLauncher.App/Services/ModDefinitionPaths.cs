namespace PdxLauncher.App.Services;

using System;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// 一键导出的路径与匹配纯逻辑（第 3 部分）：gameId 白名单、落盘子目录、
/// 越界校验、导出文件名识别。只依赖 System.*，可被自检工程原样链接。
/// <para>
/// 全程全本地：不构造 URL、不发 HTTP。目标根由调用方传入（必须是解析后的
/// <c>AppServices.DataRoot</c>），本类绝不硬编码 exeDir\data。
/// </para>
/// </summary>
public static class ModDefinitionPaths
{
    /// <summary>gameId 白名单：只允许字母、数字、下划线和连字符，防止路径穿越。</summary>
    private static readonly Regex SafeGameIdPattern = new(
        "^[A-Za-z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>脚本文档与数据类型文档两类导出。</summary>
    public enum ExportKind
    {
        /// <summary>script_docs 命令生成的脚本文档。</summary>
        ScriptDocs,

        /// <summary>dump_data_types 命令生成的数据类型文档。</summary>
        DataTypes,
    }

    /// <summary>把 gameId 归一成可安全拼进路径的片段；非法字符替换为下划线，空值兜底为 unknown。</summary>
    public static string SanitizeGameId(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return "unknown";
        }

        var trimmed = gameId.Trim();
        return SafeGameIdPattern.IsMatch(trimmed) ? trimmed : Regex.Replace(trimmed, "[^A-Za-z0-9_-]", "_");
    }

    /// <summary>
    /// 导出落盘子目录：&lt;dataRoot&gt;\mod-definitions\&lt;安全 gameId&gt;\&lt;kind&gt;。
    /// 只做路径拼接，不写盘、不建目录；调用方负责 EnsureDirectory 与越界校验。
    /// </summary>
    public static string DestinationRoot(string dataRoot, string gameId, ExportKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var kindName = kind == ExportKind.DataTypes ? "dump_data_types" : "script_docs";
        return Path.Combine(dataRoot, "mod-definitions", SanitizeGameId(gameId), kindName);
    }

    /// <summary>
    /// 判断 path 是否落在 root 内（含 root 自身）。用去尾分隔符 + 忽略大小写的前缀比较，
    /// 避免 Windows 盘符大小写与尾部斜杠造成的误判。root 与 path 都应是绝对路径。
    /// </summary>
    public static bool IsUnderRoot(string? path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 必须带目录分隔符边界：否则 ...\mod-definitions-evil 会被当成 ...\mod-definitions 的子目录。
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按文件名识别导出类别：script_docs* 归脚本文档，dump_data_types* 归数据类型文档，
    /// 其余返回 null。匹配只看文件名（不含目录），忽略大小写。
    /// </summary>
    public static ExportKind? MatchKind(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileName(fileName);
        if (name.StartsWith("script_docs", StringComparison.OrdinalIgnoreCase))
        {
            return ExportKind.ScriptDocs;
        }

        if (name.StartsWith("dump_data_types", StringComparison.OrdinalIgnoreCase))
        {
            return ExportKind.DataTypes;
        }

        return null;
    }
}
