namespace PdxLauncher.App.ViewModels;

using PdxLauncher.Core.Saves;

/// <summary>
/// 一行存档的不可变快照：先有粗略文件信息，第二段解析成功后再补一份
/// <see cref="SaveGameMeta"/> 细节。
/// <para>
/// <see cref="WithDetail"/> 在已有细节时<b>原样返回</b>：批次乱序、重复回填或迟到结果都不能把
/// 已经填好的行覆盖回退（尤其不能把 Ok 覆盖成 Degraded）。这条规则是纯函数，自检可直接钉住。
/// </para>
/// </summary>
public readonly record struct SaveRowState(SaveFileEntry File, SaveGameMeta? Detail)
{
    /// <summary>由粗略行创建（还没有细节）。</summary>
    public static SaveRowState FromFile(SaveFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new SaveRowState(file, null);
    }

    /// <summary>是否已经填过细节。同一行只允许从「无」到「有」一次。</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>补细节；已有细节时返回当前实例（不覆盖、不回退）。</summary>
    public SaveRowState WithDetail(SaveGameMeta detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return HasDetail ? this : new SaveRowState(File, detail);
    }
}
