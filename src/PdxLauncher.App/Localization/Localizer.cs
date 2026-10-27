namespace PdxLauncher.App.Localization;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using PdxLauncher.Core.Configuration;

/// <summary>
/// 界面文案入口。每个文案键对应一个只读属性（生成的属性在同名 partial 的 Strings.g.cs 里），
/// XAML 用 <c>{x:Bind L.SomeKey, Mode=OneWay}</c> 绑定，切语言时靠 <see cref="PropertyChanged"/> 整体刷新。
/// 文案表由 tools/gen-i18n.py 从 tools/i18n/*/*.json 生成，不要在代码里手写界面文字。
/// </summary>
public sealed partial class Localizer : INotifyPropertyChanged
{
    private static readonly Lazy<Localizer> LazyInstance = new(() => new Localizer());

    /// <summary>需要跟随语言刷新的视图模型；只存弱引用，页面/模型回收后自动失效。</summary>
    private readonly List<WeakReference<LocalizedObservableObject>> _localizedViews = new();

    private IReadOnlyDictionary<string, string> _table = TableZhCN;
    private string _language = UiLanguages.Default;

    private Localizer()
    {
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>语言真正变化后触发一次；各 ViewModel 据此普通知自身属性并重建选项列表。</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>全局唯一实例（文案是全局状态，不做实例化注入）。</summary>
    public static Localizer Instance => LazyInstance.Value;

    /// <summary>当前生效的界面语言键：zh-CN 或 en-US。</summary>
    public string Language => _language;

    /// <summary>
    /// 切换界面语言。null / 空白表示跟随系统 UI 语言；只影响 UI culture，不动 CurrentCulture
    /// （数字/日期格式仍按系统设置，避免 12.5 变成 12,5）。
    /// </summary>
    public void Apply(string? languageKey)
    {
        var language = UiLanguages.Normalize(languageKey);
        var table = string.Equals(language, UiLanguages.English, StringComparison.OrdinalIgnoreCase) ? TableEnUS : TableZhCN;
        var changed = !string.Equals(_language, language, StringComparison.Ordinal) || !ReferenceEquals(_table, table);
        _language = language;
        _table = table;

        try
        {
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            // 理论上不可达：language 只可能是刚归一化过的 zh-CN / en-US。
        }

        if (!changed)
        {
            return;
        }

        foreach (var (_, identifier) in AllKeys)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(identifier));
        }

        NotifyLocalizedViews();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 登记一个跟随语言刷新的 <see cref="LocalizedObservableObject"/>。
    /// 只保存弱引用：视图模型没有其它引用时会被正常回收，注册表不会把它钉在内存里。
    /// </summary>
    internal void RegisterLocalized(LocalizedObservableObject target)
    {
        lock (_localizedViews)
        {
            _localizedViews.Add(new WeakReference<LocalizedObservableObject>(target));
        }
    }

    /// <summary>遍历登记表让每个存活的视图模型重发属性通知，顺手清掉已回收的项。</summary>
    private void NotifyLocalizedViews()
    {
        List<LocalizedObservableObject> alive;
        lock (_localizedViews)
        {
            alive = new List<LocalizedObservableObject>(_localizedViews.Count);
            for (var index = _localizedViews.Count - 1; index >= 0; index--)
            {
                if (_localizedViews[index].TryGetTarget(out var target))
                {
                    alive.Add(target);
                }
                else
                {
                    _localizedViews.RemoveAt(index);
                }
            }
        }

        foreach (var target in alive)
        {
            target.RefreshLocalizedText();
        }
    }

    /// <summary>按文案键取文本，支持 {0} 占位；缺键时依次回落 en-US、zh-CN，最后返回 ⟨key⟩。</summary>
    public string T(string key, params object?[] args) => Format(key, args);

    /// <summary>生成属性与内部调用的实现：查表 → 回落 → 格式化。</summary>
    private string Format(string key, object?[]? args = null)
    {
        var template = Lookup(key);
        if (template is null)
        {
            Debug.Fail("缺少界面文案键: " + key);
            return "⟨" + key + "⟩";
        }

        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            // 占位符写错不能崩界面：退回未格式化的模板。
            return template;
        }
    }

    private string? Lookup(string key)
    {
        if (_table.TryGetValue(key, out var text))
        {
            return text;
        }

        if (TableEnUS.TryGetValue(key, out var english))
        {
            return english;
        }

        return TableZhCN.TryGetValue(key, out var chinese) ? chinese : null;
    }
}
