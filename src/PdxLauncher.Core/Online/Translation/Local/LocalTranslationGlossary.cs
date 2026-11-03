namespace PdxLauncher.Core.Online.Translation.Local;

using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 本地翻译术语词典：读、写、默认值。
/// <para>
/// 存在数据根目录下的 <c>translation\translation-glossary.json</c>（纯文本 JSON，
/// 不含敏感信息），用户可以在设置页里逐条编辑。词典同时喂给两个本地引擎：
/// Bergamot 当作「替换表」使用，Index-Translate 当作 instTrans 的硬性约束使用。
/// </para>
/// </summary>
public sealed class LocalTranslationGlossary
{
    /// <summary>词典文件名。</summary>
    public const string FileName = "translation-glossary.json";

    /// <summary>词典子目录名（相对数据根目录）。</summary>
    public const string FolderName = "translation";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fs;
    private readonly string _path;
    private List<LocalGlossaryEntry> _entries = new();

    /// <summary>创建词典存储。</summary>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="directory">词典目录；默认取数据根目录下的 <c>translation</c>。</param>
    public LocalTranslationGlossary(IFileSystem fs, string? directory = null)
    {
        _fs = fs;
        var root = string.IsNullOrWhiteSpace(directory)
            ? System.IO.Path.Combine(PdxLauncher.Core.Configuration.PdxDataRoot.Directory, FolderName)
            : directory;
        _path = System.IO.Path.Combine(root, FileName);
        Load();
    }

    /// <summary>词典文件完整路径。</summary>
    public string Path => _path;

    /// <summary>当前词条（已去重、已忽略空词条）。</summary>
    public IReadOnlyList<LocalGlossaryEntry> Entries => _entries;

    /// <summary>预置词条：新装用户直接得到一份可改的常用表。</summary>
    public static IReadOnlyList<LocalGlossaryEntry> DefaultEntries { get; } = new[]
    {
        new LocalGlossaryEntry("mod", "模组"),
        new LocalGlossaryEntry("mods", "模组"),
        new LocalGlossaryEntry("modding", "模组开发"),
        new LocalGlossaryEntry("workshop", "创意工坊"),
        new LocalGlossaryEntry("DLC", "DLC"),
        new LocalGlossaryEntry("gameplay", "玩法"),
        new LocalGlossaryEntry("crash", "崩溃"),
    };

    /// <summary>从磁盘读取词典；文件缺失或损坏时退回预置词条。</summary>
    public void Load()
    {
        try
        {
            if (_fs.FileExists(_path))
            {
                var json = _fs.ReadAllText(_path);
                var parsed = JsonSerializer.Deserialize<List<LocalGlossaryEntry>>(json, JsonOptions);
                if (parsed is not null)
                {
                    _entries = Normalize(parsed);
                    return;
                }
            }
        }
        catch (Exception)
        {
            // 词典损坏不该挡住启动：退回预置词条，用户在设置页还能重新编辑。
        }

        _entries = Normalize(DefaultEntries.ToList());
    }

    /// <summary>覆盖保存词典；词条为空时写空表，不写预置值。</summary>
    public void Save(IReadOnlyList<LocalGlossaryEntry> entries)
    {
        _entries = Normalize(entries.ToList());
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            _fs.EnsureDirectory(dir);
        }

        _fs.WriteAllTextAtomic(_path, JsonSerializer.Serialize(_entries, JsonOptions));
    }

    /// <summary>把词条转成可直接喂给 Bergamot 的替换表。</summary>
    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
        {
            map[entry.Source] = entry.Target;
        }

        return map;
    }

    private static List<LocalGlossaryEntry> Normalize(List<LocalGlossaryEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LocalGlossaryEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                continue;
            }

            var source = entry.Source?.Trim() ?? string.Empty;
            var target = entry.Target?.Trim() ?? string.Empty;
            if (source.Length == 0 || target.Length == 0)
            {
                continue;
            }

            if (!seen.Add(source))
            {
                continue;
            }

            result.Add(new LocalGlossaryEntry(source, target));
        }

        return result;
    }
}
