namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 设置页「术语词典」里的一行：一条「原文词条 → 译文词条」。
/// <para>
/// 词条本身可以就地编辑，改完由页面上的「保存词典」统一落盘。
/// </para>
/// </summary>
public sealed class GlossaryEntryViewModel : LocalizedObservableObject
{
    private string _source = string.Empty;
    private string _target = string.Empty;

    /// <summary>创建词条行。</summary>
    /// <param name="source">原文词条。</param>
    /// <param name="target">译文词条。</param>
    public GlossaryEntryViewModel(string source, string target)
    {
        _source = source;
        _target = target;
    }

    /// <summary>原文词条（例如 mod）。</summary>
    public string Source
    {
        get => _source;
        set => SetProperty(ref _source, value);
    }

    /// <summary>译文词条（例如 模组）。</summary>
    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    /// <summary>转成词典的存储结构。</summary>
    public LocalGlossaryEntry ToEntry() => new(Source.Trim(), Target.Trim());
}
