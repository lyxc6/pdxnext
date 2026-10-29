namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Contracts;

/// <summary>
/// Paradox 账号的会话状态机：登录 / 续期 / 登出，并把「当前会话」广播给界面。
/// <para>
/// 会话落盘由 <see cref="PdxSessionStore"/> 负责（DPAPI 保护），网络由 <see cref="PdxAccountClient"/>
/// 负责；本类只协调两者并暴露一个可观察的状态，避免设置页与 Mod 库页各持一份会话。
/// </para>
/// <para>
/// 冷启动续期在后台进行，失败一律静默降级为未登录（不弹窗、不阻断启动）。
/// </para>
/// </summary>
public sealed class AccountService
{
    private readonly IAppLogger _logger;
    private readonly PdxSessionStore _store;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PdxAccountClient? _client;
    private PdxSession? _session;
    private bool _signingIn;

    /// <summary>创建服务。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="store">会话落盘。</param>
    /// <param name="clock">时钟（自检注入固定时间）。</param>
    /// <param name="client">账号客户端；代理重建时由 <see cref="ReplaceClient"/> 替换。</param>
    public AccountService(IAppLogger logger, PdxSessionStore store, IClock clock, PdxAccountClient client)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(client);

        _logger = logger;
        _store = store;
        _clock = clock;
        _client = client;
    }

    /// <summary>当前会话；未登录为 null。</summary>
    public PdxSession? Session => _session;

    /// <summary>是否已登录。</summary>
    public bool IsSignedIn => _session is not null;

    /// <summary>正在登录（界面据此禁用按钮）。</summary>
    public bool IsBusy => _signingIn;

    /// <summary>登录状态变化时触发；订阅方自行 marshal 到 UI 线程。</summary>
    public event EventHandler? SessionChanged;

    /// <summary>代理链路重建后换掉底层客户端（会话本身保留）。</summary>
    public void ReplaceClient(PdxAccountClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>启动时读回上次的会话并尽力续期；任何失败都静默降级为未登录。</summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var stored = _store.Load();
        if (stored is null)
        {
            return;
        }

        if (!stored.NeedsRefresh(_clock.UtcNow) || !stored.CanRefresh)
        {
            _session = stored;
            RaiseChanged();
            _logger.Info("已恢复 Paradox 会话（" + stored.Email + "）。");
            return;
        }

        var refreshed = await _client!.RefreshAsync(stored, cancellationToken).ConfigureAwait(false);
        if (refreshed.IsSuccess && refreshed.Value is { } fresh)
        {
            _session = fresh;
            _store.Save(fresh);
            RaiseChanged();
            _logger.Info("Paradox 会话已续期（" + fresh.Email + "）。");
            return;
        }

        // 续期失败的原因只记日志：用户可能是离线启动，不该在启动时弹错误。
        _logger.Warn("Paradox 会话续期失败，按未登录处理：" + refreshed.ErrorMessage);
        _session = null;
        _store.Clear();
        RaiseChanged();
    }

    /// <summary>邮箱 + 密码登录。</summary>
    /// <param name="email">邮箱。</param>
    /// <param name="password">明文密码（只在本次调用内使用）。</param>
    /// <param name="rememberMe">是否请求续期 token。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<PdxSession>> SignInAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _signingIn = true;
            RaiseChanged();

            var result = await _client!
                .SignInAsync(email, password, rememberMe, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is not { } session)
            {
                return result;
            }

            _session = session;
            if (rememberMe)
            {
                _store.Save(session);
            }
            else
            {
                // 不勾「保持登录」就只在本次运行有效（与 account.rememberHint 的承诺一致）：
                // 顺手删掉上一次留下的会话文件，否则重启后仍会被 RestoreAsync 恢复出来。
                _store.Clear();
            }

            RaiseChanged();
            _logger.Info("Paradox 登录成功（" + session.Email + "）。");
            return result;
        }
        finally
        {
            _signingIn = false;
            _gate.Release();
            RaiseChanged();
        }
    }

    /// <summary>手动续期（界面的「刷新会话」）。</summary>
    public async Task<AppResult<PdxSession>> RefreshAsync(CancellationToken cancellationToken)
    {
        var current = _session;
        if (current is null)
        {
            return AppResult<PdxSession>.Fail(ErrorKind.Unauthorized, "pdx-refresh-no-session");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _client!.RefreshAsync(current, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Value is { } fresh)
            {
                _session = fresh;
                _store.Save(fresh);
                RaiseChanged();
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>登出：清内存与磁盘。服务端会话不强制吊销（官方同样只在本地清）。</summary>
    public void SignOut()
    {
        _session = null;
        _store.Clear();
        _logger.Info("已登出 Paradox 账号。");
        RaiseChanged();
    }

    private void RaiseChanged() => SessionChanged?.Invoke(null, EventArgs.Empty);
}
