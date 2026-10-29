namespace PdxLauncher.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;

/// <summary>登录反馈的严重级别，页面据此选 InfoBar 的 Severity。</summary>
public enum AccountStatusLevel
{
    /// <summary>普通提示。</summary>
    Info,

    /// <summary>操作成功。</summary>
    Success,

    /// <summary>操作失败。</summary>
    Error
}

/// <summary>
/// 「账号」页的视图模型：邮箱 / 密码登录、退出、状态文案。
/// <para>
/// 账号页是外壳里独立的一页（<c>NavigationService.AccountTag</c>），不再是设置页的一个分节；
/// 会话恢复统一在启动时由 <c>App.xaml.cs</c> 触发，本类只负责把当前会话状态翻译成界面文案。
/// </para>
/// 本类不引用任何 Microsoft.UI.Xaml 类型。
/// </summary>
public sealed partial class AccountViewModel : LocalizedObservableObject
{
    /// <summary>邮箱输入框的内容（只在内存里，未落盘）。</summary>
    [ObservableProperty]
    private string _email = string.Empty;

    /// <summary>是否保持登录（勾了就向服务端要 refresh token 并落盘）。</summary>
    [ObservableProperty]
    private bool _remember;

    /// <summary>一次性操作反馈（登录成功 / 退出成功）；没有反馈时为空串。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>上一次状态文案对应的键与参数：切语言后按新语言重建同一句话。</summary>
    private string? _statusKey;

    private object?[] _statusArgs = [];

    private AccountStatusLevel _statusLevel = AccountStatusLevel.Info;

    public AccountViewModel()
    {
        if (AppServices.IsInitialized)
        {
            _remember = AppServices.Settings.PdxRememberMe;
        }
    }

    /// <summary>登录中 / 登出中，界面据此禁用输入。</summary>
    public bool IsBusy => AppServices.IsInitialized && AppServices.Account.IsBusy;

    /// <summary>已登录。</summary>
    public bool IsSignedIn => AppServices.IsInitialized && AppServices.Account.IsSignedIn;

    /// <summary>
    /// 输入控件（邮箱框 / 密码框 / 保持登录）是否可用：未在忙、未登录。
    /// 与 <see cref="CanSignIn"/> 分开是因为邮箱初值为空 —— 若输入框也绑 <see cref="CanSignIn"/>，
    /// 邮箱框会在启动时禁用自己，用户根本没有地方输入邮箱（曾经的「点了没反应」根因）。
    /// </summary>
    public bool CanEditCredentials => AccountFormState.CanEditCredentials(IsBusy, IsSignedIn);

    /// <summary>能否提交登录：可编辑输入，且邮箱非空。</summary>
    public bool CanSignIn => AccountFormState.CanSubmit(IsBusy, IsSignedIn, Email);

    /// <summary>没登录时显示登录表单。</summary>
    public bool ShowSignInPanel => !IsSignedIn;

    /// <summary>已登录时显示退出面板。</summary>
    public bool ShowSignedInPanel => IsSignedIn;

    /// <summary>账号状态文案：未登录 / 正在登录 / 已登录（含显示名与邮箱）。</summary>
    public string StatusText
    {
        get
        {
            if (!AppServices.IsInitialized)
            {
                return T(Keys.AccountStatusNotReady);
            }

            var account = AppServices.Account;
            if (account.IsSignedIn && account.Session is { } session)
            {
                return T(Keys.AccountStatusSignedIn, session.DisplayName, session.Email);
            }

            return account.IsBusy
                ? T(Keys.AccountStatusSigningIn)
                : T(Keys.AccountStatusHint);
        }
    }

    /// <summary>有没有要展示的登录反馈；有就打开页内 InfoBar。</summary>
    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>反馈级别。</summary>
    public AccountStatusLevel StatusLevel => _statusLevel;

    /// <summary>
    /// 头像首字母：已登录取显示名首字，否则取邮箱首字，都没有就用占位的 P。
    /// </summary>
    public string Initials
    {
        get
        {
            if (!AppServices.IsInitialized)
            {
                return "P";
            }

            var account = AppServices.Account;
            var name = account.IsSignedIn ? account.Session?.DisplayName : Email;
            var trimmed = (name ?? string.Empty).Trim();
            return trimmed.Length == 0
                ? "P"
                : char.ToUpperInvariant(trimmed[0]).ToString();
        }
    }

    /// <summary>身份卡上的名字：已登录用显示名，未登录用「未登录」。</summary>
    public string ProfileName
    {
        get
        {
            if (AppServices.IsInitialized
                && AppServices.Account.Session is { } session
                && !string.IsNullOrWhiteSpace(session.DisplayName))
            {
                return session.DisplayName;
            }

            return T(Keys.AccountNotSignedInTitle);
        }
    }

    /// <summary>
    /// 登录。成功返回 null，失败返回可直接显示的错误文案。
    /// </summary>
    /// <param name="password">密码框里的明文（不保留）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<string?> SignInAsync(string password, CancellationToken cancellationToken)
    {
        if (!AppServices.IsInitialized)
        {
            return T(Keys.AccountStatusNotReady);
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            return T(Keys.AccountErrorEmailRequired);
        }

        if (string.IsNullOrEmpty(password))
        {
            return T(Keys.AccountErrorPasswordRequired);
        }

        RaiseAccountProps();
        try
        {
            var result = await AppServices.Account
                .SignInAsync(Email.Trim(), password, Remember, cancellationToken)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                var reason = DescribeAccountError(result.ErrorMessage);
                SetStatus(Keys.AccountErrorSignInFailed, AccountStatusLevel.Error, reason);
                return T(Keys.AccountErrorSignInFailed, reason);
            }

            SetStatus(Keys.AccountStatusSignInOk, AccountStatusLevel.Success);
            return null;
        }
        finally
        {
            RaiseAccountProps();
        }
    }

    /// <summary>登出（本地清空，不影响服务端会话）。</summary>
    public void SignOut()
    {
        if (!AppServices.IsInitialized)
        {
            return;
        }

        AppServices.Account.SignOut();
        RaiseAccountProps();
        SetStatus(Keys.AccountStatusSignOutOk, AccountStatusLevel.Success);
    }

    /// <summary>直接显示一句错误（页面兜底用，不走翻译键）。</summary>
    public void ShowStatus(string message)
    {
        _statusKey = null;
        _statusArgs = [];
        _statusLevel = AccountStatusLevel.Error;
        StatusMessage = message;
        OnPropertyChanged(nameof(StatusLevel));
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    /// <summary>会话状态可能变了（启动恢复完成 / 别的页面登录）：通知所有派生属性。</summary>
    public void RaiseAccountProps()    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(CanEditCredentials));
        OnPropertyChanged(nameof(CanSignIn));
        OnPropertyChanged(nameof(ShowSignInPanel));
        OnPropertyChanged(nameof(ShowSignedInPanel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusLevel));
        OnPropertyChanged(nameof(HasStatusMessage));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(ProfileName));
    }

    /// <summary>把 Core 的错误码翻成给用户看的那一句（错误码本身进日志）。</summary>
    private string DescribeAccountError(string? errorMessage)
    {
        var code = errorMessage ?? string.Empty;
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Warn("Paradox 账号操作失败：" + code);
        }

        // 归类放在 AccountErrorText 里（纯 C#，可自检），免得再漏掉 login/refresh 前缀的差异。
        return AccountErrorText.Describe(code) switch
        {
            AccountErrorCategory.BadCredentials => T(Keys.AccountErrorBadCredentials),
            AccountErrorCategory.Network => T(Keys.AccountErrorNetwork),
            _ => code,
        };
    }

    /// <summary>记下状态键、参数与级别，切语言后能按新语言重建同一句。</summary>
    private void SetStatus(string key, AccountStatusLevel level = AccountStatusLevel.Info, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        _statusLevel = level;
        StatusMessage = T(key, args);
        OnPropertyChanged(nameof(StatusLevel));
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    partial void OnEmailChanged(string value)
    {
        OnPropertyChanged(nameof(CanSignIn));
        OnPropertyChanged(nameof(Initials));
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    partial void OnRememberChanged(bool value)
    {
        if (!AppServices.IsInitialized)
        {
            return;
        }

        AppServices.Settings.PdxRememberMe = value;
        AppServices.SaveSettingsDeferred();
    }

    /// <summary>语言切换后：状态文案与状态栏都要按新语言重建。</summary>
    protected override void Relocalize()
    {
        RaiseAccountProps();
        if (_statusKey is not null)
        {
            StatusMessage = T(_statusKey, _statusArgs);
        }
    }
}
