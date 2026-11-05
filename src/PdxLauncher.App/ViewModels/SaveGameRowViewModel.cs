namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Saves;

/// <summary>
/// 存档列表的一行：只把 <see cref="SaveGameMeta"/> 契约翻译成可绑定的 string / bool，
/// 不引用任何 Microsoft.UI.Xaml 类型（可见性由 XAML 侧的 XamlVisibility 决定）。
/// <para>
/// 元数据缺失时<b>不</b>显示空白：日期回退到文件修改时间并明确标注来源，
/// 解析失败的档显示「元数据不可读」，启发式格式另有可见的弱化标记，三者互不冒充精确数据。
/// </para>
/// </summary>
public sealed class SaveGameRowViewModel : LocalizedObservableObject
{
    /// <summary>
    /// 元数据解析是启发式的游戏（V3 命名档、EU5 部分档、CS2 的 .cok JSON 元数据）。
    /// 这些游戏的存档行必须带可见的弱化标记（文案来自 <c>saves.experimental</c>）；CK3 / Stellaris 不走这里。
    /// </summary>
    private static readonly HashSet<string> ExperimentalGameIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "victoria3",
        "eu5",
        "cities_skylines_2",
    };

    private readonly SaveGameMeta _meta;
    private readonly int _index;

    public SaveGameRowViewModel(SaveGameMeta meta, int index)
    {
        ArgumentNullException.ThrowIfNull(meta);
        _meta = meta;
        _index = index;
    }

    public string FileName => _meta.FileName;

    /// <summary>完整路径只作为悬停提示，不占列表宽度。</summary>
    public string FilePath => _meta.FilePath;

    /// <summary>UI 自动化 id；保持 ASCII，不随语言变化。挂在行容器（ListViewItem）上。</summary>
    public string AutomationId => "save_" + _index.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 读屏用的整行名字：文件名 + 日期 + 详情。列表项的 Content 是视图模型对象，
    /// 不设这个属性时 UIA 会把整列每一行都读成类型全名（<c>PdxLauncher.App.ViewModels.SaveGameRowViewModel</c>）。
    /// 用 i18n 的成对连接片段拼，中文用「，」、英文用「, 」，不写死任何一侧的标点。
    /// </summary>
    public string AutomationName
    {
        get
        {
            var name = T(Keys.SavesRowNamePair, FileName, DateText);
            return HasDetails ? T(Keys.SavesRowNamePair, name, DetailsText) : name;
        }
    }

    public bool IsExperimental => ExperimentalGameIds.Contains(_meta.GameId);

    public string ExperimentalText => T(Keys.SavesExperimental);

    public string ExperimentalTooltip => T(Keys.SavesExperimentalTooltip);

    public bool IsDegraded => _meta.ParseState == SaveParseState.Degraded;

    public string DegradedText => T(Keys.SavesMetadataUnreadable);

    public bool HasTags => IsExperimental || IsDegraded;

    public bool HasGameDate => !string.IsNullOrWhiteSpace(_meta.GameDate);

    /// <summary>游戏内日期；没有就用文件修改时间（本地时区）。</summary>
    public string DateText => HasGameDate
        ? _meta.GameDate!
        : _meta.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    /// <summary>日期来自文件修改时间时显示弱化说明，避免被当成游戏内日期。</summary>
    public bool HasDateFallback => !HasGameDate;

    public string DateFallbackText => T(Keys.SavesDateFallback);

    public string SizeText => SizeFormatter.Format(_meta.FileSize);

    public bool HasVersion => !string.IsNullOrWhiteSpace(_meta.GameVersion);

    public string VersionText => HasVersion ? T(Keys.SavesVersion, _meta.GameVersion!) : string.Empty;

    public bool HasDetails =>
        SaveRowText.HasSaveName(_meta.SaveName) || SaveRowText.ShouldShowPlace(_meta.PlayerCountry, _meta.SaveName);

    /// <summary>
    /// 存档名 / 国家（CS2 是城市）；有哪个显示哪个，都没有时行内不留空标签。
    /// 国家段与存档名相同时不重复显示。
    /// </summary>
    public string DetailsText
    {
        get
        {
            var parts = new List<string>(2);
            if (SaveRowText.HasSaveName(_meta.SaveName))
            {
                parts.Add(T(Keys.SavesSaveName, _meta.SaveName!));
            }

            if (SaveRowText.ShouldShowPlace(_meta.PlayerCountry, _meta.SaveName))
            {
                parts.Add(SaveRowText.IsCityPlace(_meta.Container)
                    ? T(Keys.SavesCity, _meta.PlayerCountry!)
                    : T(Keys.SavesCountry, _meta.PlayerCountry!));
            }

            return string.Join(" · ", parts);
        }
    }
}
