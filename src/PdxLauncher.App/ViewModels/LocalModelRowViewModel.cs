namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 设置页「本地离线模型」里的一行：模型名、体积、是否已安装，以及下载 / 删除按钮的可用状态。
/// <para>
/// 这一行只负责呈现状态；下载与删除由 <see cref="SettingsViewModel"/> 执行
/// （下载需要页面生命周期取消令牌，所以动作不放在行里）。
/// </para>
/// </summary>
public sealed class LocalModelRowViewModel : LocalizedObservableObject
{
    private readonly string _titleKey;
    private bool _isInstalled;
    private bool _isBusy;
    private string _statusText = string.Empty;

    /// <summary>创建模型行。</summary>
    /// <param name="kind">模型种类。</param>
    /// <param name="titleKey">标题的 i18n 键。</param>
    /// <param name="description">模型说明：型号与体积。</param>
    public LocalModelRowViewModel(LocalTranslationModelKind kind, string titleKey, string description)
    {
        Kind = kind;
        _titleKey = titleKey;
        Description = description;
    }

    /// <summary>模型种类。</summary>
    public LocalTranslationModelKind Kind { get; }

    /// <summary>模型名。</summary>
    public string Title => T(_titleKey);

    /// <summary>模型说明：型号与体积。</summary>
    public string Description { get; }

    /// <summary>模型是否已经装好。</summary>
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanRemove));
            }
        }
    }

    /// <summary>正在下载或删除（按钮禁用、显示进度环）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanRemove));
            }
        }
    }

    /// <summary>这一行的状态文案（已安装 / 未安装 / 下载中 N%）。</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>可以点「下载」：没装好且当前不忙。</summary>
    public bool CanDownload => !_isInstalled && !_isBusy;

    /// <summary>可以点「删除」：装好了且当前不忙。</summary>
    public bool CanRemove => _isInstalled && !_isBusy;
}
