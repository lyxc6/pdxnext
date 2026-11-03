namespace PdxLauncher.Core.Steam;

/// <summary>
/// <c>steamapps\\appmanifest_&lt;appid&gt;.acf</c> 的关键字段。
/// <see cref="InstallPath"/> = &lt;library&gt;\\steamapps\\common\\&lt;InstallDir&gt;。
/// </summary>
public sealed record AppManifest(
    int AppId,
    string Name,
    string InstallDir,
    string InstallPath,
    int StateFlags,
    long SizeOnDisk,
    string? LastOwner = null,
    IReadOnlyList<string>? DisabledDlc = null)
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>StateFlags 的 bit 2（值 4）表示"已完整安装"。Steam 用 4 表示 Installed。</summary>
    public bool IsInstalled => (StateFlags & 4) == 4;

    /// <summary>
    /// 清单里 <c>DisabledDLC</c> 的 id 集合。官方启动器读它来判断「Steam 停用了、
    /// 但账号确实买过」的 DLC，所以仍算已拥有。
    /// </summary>
    public IReadOnlySet<string> DisabledDlcSet
    {
        get
        {
            var items = DisabledDlc;
            if (items is null || items.Count == 0)
            {
                return EmptySet;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    set.Add(item.Trim());
                }
            }

            return set;
        }
    }
}
