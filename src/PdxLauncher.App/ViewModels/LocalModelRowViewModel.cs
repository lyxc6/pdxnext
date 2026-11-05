namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 设置页「本地离线模型」里的一行：模型名、体积、安装状态（五态），以及下载 / 校验 / 删除 /
/// 打开文件夹这几个按钮的可用状态。
/// <para>
/// 这一行只负责呈现状态；下载、校验与删除由 <see cref="SettingsViewModel"/> 执行
/// （都需要页面生命周期取消令牌，所以动作不放在行里）。
/// </para>
/// <para>
/// 状态文案的唯一来源是 Core 的 <see cref="LocalTranslationModelState"/>，不再用
/// <c>IsInstalled</c> 单个布尔自己拼「已安装 / 未安装」——那样会把「缺 config.txt」
/// 显示成已安装、引擎却说不可用。
/// </para>
/// </summary>
public sealed class LocalModelRowViewModel : LocalizedObservableObject
{
    private readonly string _titleKey;
    private LocalTranslationModelState? _state;
    private bool _directoryExists;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private string _operationText = string.Empty;
    private DateTimeOffset _operationExpiresAt;

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

    /// <summary>模型目录（绝对路径）；还没有状态时为 <see cref="string.Empty"/>。</summary>
    public string Directory => _state?.Directory ?? string.Empty;

    /// <summary>模型目录此刻是否存在（决定「打开文件夹」是否可点）。</summary>
    public bool DirectoryExists => _directoryExists;

    /// <summary>文件是否齐全且尺寸正确；缺 <c>config.txt</c> 时仍为 true（与 Core 同义）。</summary>
    public bool IsInstalled => _state?.IsInstalled == true;

    /// <summary>引擎是否真的能加载：文件齐、尺寸对、配置也在。</summary>
    public bool IsRunnable => _state?.IsRunnable == true;

    /// <summary>正在下载 / 校验 / 删除（按钮禁用、显示进度环）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseStateProperties();
            }
        }
    }

    /// <summary>
    /// 这一行常驻的安装状态文案（按 Core 五态区分：未安装 / 缺文件 / 尺寸不符 /
    /// 缺配置 / 已安装）。语言切换时由 <see cref="Relocalize"/> 按新语言重建。
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// 一次操作（下载 / 校验 / 删除）的结果或进度文案。与 <see cref="StatusText"/> 分开，
    /// 到 <see cref="OperationExpiresAt"/> 后由 ViewModel 清空，不会一直挂在界面上。
    /// </summary>
    public string OperationText
    {
        get => _operationText;
        private set
        {
            if (SetProperty(ref _operationText, value))
            {
                OnPropertyChanged(nameof(HasOperationText));
            }
        }
    }

    /// <summary>操作文案的过期时刻；没有操作文案时为 <see cref="DateTimeOffset.MinValue"/>。</summary>
    public DateTimeOffset OperationExpiresAt => _operationExpiresAt;

    /// <summary>当前是否有操作结果 / 进度文案。</summary>
    public bool HasOperationText => !string.IsNullOrWhiteSpace(_operationText);

    /// <summary>可以点「下载」：引擎跑不起来（没装、缺文件、尺寸不符或缺配置）且当前不忙。</summary>
    public bool CanDownload => !IsRunnable && !_isBusy;

    /// <summary>可以点「删除」：以 <c>IsInstalled</c> 为准（缺配置也应该能删）且当前不忙。</summary>
    public bool CanRemove => IsInstalled && !_isBusy;

    /// <summary>可以点「校验」：已安装或尺寸不符时才有意义；未安装 / 缺文件时禁用。</summary>
    public bool CanVerify => _state is not null
        && _state.Status is LocalTranslationModelInstallStatus.Installed or LocalTranslationModelInstallStatus.SizeMismatch
        && !_isBusy;

    /// <summary>可以点「打开文件夹」：目录存在且当前不忙。</summary>
    public bool CanOpenFolder => _directoryExists && !_isBusy;

    /// <summary>
    /// 应用 Core 给出的安装状态快照，并刷新状态文案与按钮可用性。
    /// </summary>
    /// <param name="state">安装状态。</param>
    /// <param name="directoryExists">模型目录此刻是否存在（由调用方查文件系统）。</param>
    public void ApplyState(LocalTranslationModelState state, bool directoryExists)
    {
        _state = state;
        _directoryExists = directoryExists;
        StatusText = BuildStatusText();
        RaiseStateProperties();
    }

    /// <summary>写入一条操作文案；<paramref name="expiresAt"/> 为 null 表示暂时不自动清除（如下载进度）。</summary>
    /// <param name="text">文案。</param>
    /// <param name="expiresAt">过期时刻；null 表示不自动清除。</param>
    public void SetOperation(string text, DateTimeOffset? expiresAt = null)
    {
        _operationExpiresAt = expiresAt ?? DateTimeOffset.MinValue;
        OperationText = text;
    }

    /// <summary>清空操作文案（开始下一次操作或页面刷新时）。</summary>
    public void ClearOperation()
    {
        _operationExpiresAt = DateTimeOffset.MinValue;
        OperationText = string.Empty;
    }

    /// <summary>到点后清空操作文案；返回是否真的清掉了。</summary>
    /// <param name="now">当前时刻。</param>
    public bool ExpireOperation(DateTimeOffset now)
    {
        if (_operationExpiresAt == DateTimeOffset.MinValue || now < _operationExpiresAt)
        {
            return false;
        }

        ClearOperation();
        return true;
    }

    /// <summary>语言切换后按新语言重建状态文案（操作文案是已经拼好的句子，等它自然过期）。</summary>
    protected override void Relocalize() => StatusText = BuildStatusText();

    private string BuildStatusText()
    {
        if (_state is null)
        {
            return T(Keys.SettingsTranslateLocalModelNotInstalled);
        }

        return _state.Status switch
        {
            LocalTranslationModelInstallStatus.Installed =>
                T(Keys.SettingsTranslateLocalModelInstalled),
            LocalTranslationModelInstallStatus.MissingFiles =>
                T(
                    Keys.SettingsTranslateLocalModelStatusMissingFiles,
                    _state.MissingFiles.Count,
                    string.Join(", ", _state.MissingFiles)),
            LocalTranslationModelInstallStatus.SizeMismatch =>
                BuildSizeMismatchText(_state.SizeMismatches),
            LocalTranslationModelInstallStatus.ConfigMissing =>
                T(Keys.SettingsTranslateLocalModelStatusConfigMissing),
            LocalTranslationModelInstallStatus.NotInstalled =>
                T(Keys.SettingsTranslateLocalModelNotInstalled),
            _ => T(Keys.SettingsTranslateLocalModelNotInstalled),
        };
    }

    private static string BuildSizeMismatchText(IReadOnlyList<LocalTranslationModelSizeMismatch> mismatches)
    {
        if (mismatches.Count == 0)
        {
            return T(Keys.SettingsTranslateLocalModelStatusSizeMismatch, 0, string.Empty, string.Empty, string.Empty);
        }

        var first = mismatches[0];
        return T(
            Keys.SettingsTranslateLocalModelStatusSizeMismatch,
            mismatches.Count,
            first.FileName,
            SizeFormatter.Format(first.ActualBytes),
            SizeFormatter.Format(first.ExpectedBytes));
    }

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(Directory));
        OnPropertyChanged(nameof(DirectoryExists));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsRunnable));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(CanOpenFolder));
        OnPropertyChanged(nameof(OperationExpiresAt));
    }
}
