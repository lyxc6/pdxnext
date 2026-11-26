namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Mcp;

/// <summary>
/// 「MCP 访问密钥」子页的 ViewModel：新建 / 编辑共用一套能力勾选表单。
/// 页面只负责提交与回传，读写库全部在这里完成。
/// </summary>
public sealed class McpKeyEditorViewModel : LocalizedObservableObject
{
    private McpAccessKeyStore? _store;
    private McpAccessKey? _original;
    private DateTimeOffset? _expiresUtc;
    private string _errorMessage = string.Empty;
    private string _createdToken = string.Empty;
    private string _displayName = string.Empty;

    public McpKeyEditorViewModel(string? keyId)
    {
        KeyId = keyId;
        ApplyCapabilities(McpCapabilityCatalog.ReadOnly);
    }

    /// <summary>
    /// 绑定数据库并载入编辑对象。页面在导航参数到达后调用；
    /// 找不到 keyId 对应的行时按新建处理，避免打开一个空壳编辑页。
    /// </summary>
    public void Initialize(McpAccessKeyStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _original = KeyId is null
            ? null
            : store.List().FirstOrDefault(key => string.Equals(key.Id, KeyId, StringComparison.Ordinal));
        if (_original is not null)
        {
            _displayName = _original.DisplayName;
            _expiresUtc = _original.ExpiresUtc;
            ApplyCapabilities(_original.CapabilityList);
        }

        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsExisting));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(KeyPrefix));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ExpiryText));
        OnPropertyChanged(nameof(HasExpiry));
        OnPropertyChanged(nameof(GamesReadEnabled));
        OnPropertyChanged(nameof(UploadReadEnabled));
        OnPropertyChanged(nameof(UploadWriteEnabled));
        OnPropertyChanged(nameof(PlaySetsReadEnabled));
        OnPropertyChanged(nameof(PlaySetsPlanEnabled));
        OnPropertyChanged(nameof(SettingsReadEnabled));
        OnPropertyChanged(nameof(SettingsPlanEnabled));
        OnPropertyChanged(nameof(SelectedCapabilitiesText));
        OnPropertyChanged(nameof(CanSaveName));
        OnPropertyChanged(nameof(HasWriteCapability));
    }

    /// <summary>null 表示新建。</summary>
    public string? KeyId { get; }

    public bool IsNew => _original is null;

    public bool IsExisting => !IsNew;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                OnPropertyChanged(nameof(CanSaveName));
            }
        }
    }

    /// <summary>名称非空且至少勾选一个能力才允许保存。</summary>
    public bool CanSaveName => _displayName.Trim().Length > 0 && SelectedCapabilities().Count > 0;

    public string KeyPrefix => _original?.KeyPrefix ?? T(Keys.SettingsMcpKeyPrefixNew);

    public string StatusText => _original?.StatusText ?? T(Keys.SettingsMcpKeyStatusNew);

    public string ExpiryText => _expiresUtc is { } expiry
        ? expiry.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : T(Keys.SettingsMcpKeyNoExpiry);

    public bool HasExpiry => _expiresUtc is not null;

    public DateTimeOffset? ExpiresUtc => _expiresUtc;

    /// <summary>新建 / 轮换后的一次性明文；只在本页展示一次。</summary>
    public string CreatedToken
    {
        get => _createdToken;
        private set
        {
            if (SetProperty(ref _createdToken, value))
            {
                OnPropertyChanged(nameof(HasCreatedToken));
            }
        }
    }

    public bool HasCreatedToken => _createdToken.Length > 0;

    public string ErrorMessage => _errorMessage;

    public bool HasError => _errorMessage.Length > 0;

    // 七个能力复选框各自一个公开属性，x:Bind TwoWay 直接写回。
    public bool GamesReadEnabled { get; set; }
    public bool UploadReadEnabled { get; set; }
    public bool UploadWriteEnabled { get; set; }
    public bool PlaySetsReadEnabled { get; set; }
    public bool PlaySetsPlanEnabled { get; set; }
    public bool SettingsReadEnabled { get; set; }
    public bool SettingsPlanEnabled { get; set; }

    /// <summary>复选框变化后由页面调用：刷新摘要、保存可用性与写入风险提示。</summary>
    public void OnCapabilityChanged()
    {
        OnPropertyChanged(nameof(SelectedCapabilitiesText));
        OnPropertyChanged(nameof(CanSaveName));
        OnPropertyChanged(nameof(HasWriteCapability));
    }

    /// <summary>勾选任一写入能力就显示「AI 写入仍要你确认」的提示。</summary>
    public bool HasWriteCapability => UploadWriteEnabled || PlaySetsPlanEnabled || SettingsPlanEnabled;

    public static string CapabilityLabel(string capability) => T(KeysFor(capability).Title);

    public static string CapabilityDescription(string capability) => T(KeysFor(capability).Description);

    /// <summary>列表摘要用中文能力名，而不是库里的原始字符串。</summary>
    public string SelectedCapabilitiesText
    {
        get
        {
            var labels = McpCapabilityCatalog.All
                .Where(IsSelected)
                .Select(CapabilityLabel)
                .ToArray();
            return labels.Length == 0
                ? T(Keys.SettingsMcpKeySummaryNone)
                : string.Join(T(Keys.SettingsMcpKeySummarySeparator), labels);
        }
    }

    public IReadOnlyList<string> SelectedCapabilities() =>
        McpCapabilityCatalog.All.Where(IsSelected).ToArray();

    public void SetExpiry(DateTimeOffset? expiry)
    {
        _expiresUtc = expiry;
        OnPropertyChanged(nameof(ExpiryText));
        OnPropertyChanged(nameof(HasExpiry));
        OnPropertyChanged(nameof(ExpiresUtc));
    }

    public void ClearExpiry() => SetExpiry(null);

    /// <summary>
    /// 保存。新建返回一次性密钥；编辑普通字段返回 null。
    /// 空名称 / 空能力在界面上拦下，避免生成一把什么都读不到的钥匙。
    /// </summary>
    public McpCreatedAccessKey? Save()
    {
        if (_store is null)
        {
            SetError(T(Keys.SettingsMcpStatusUnavailable));
            return null;
        }

        SetError(string.Empty);
        var name = _displayName.Trim();
        if (name.Length == 0)
        {
            SetError(T(Keys.SettingsMcpKeyNameRequired));
            return null;
        }

        var capabilities = SelectedCapabilities();
        if (capabilities.Count == 0)
        {
            SetError(T(Keys.SettingsMcpKeyCapabilityRequired));
            return null;
        }

        try
        {
            if (_original is null)
            {
                var created = _store.Create(name, capabilities, _expiresUtc);
                CreatedToken = created.RawToken;
                return created;
            }

            _store.Rename(_original.Id, name);
            _store.UpdateCapabilities(_original.Id, capabilities);
            _store.UpdateExpiry(_original.Id, _expiresUtc);
            return null;
        }
        catch (Exception exception)
        {
            AppServices.Logger.Warn("保存 MCP 访问密钥失败：" + exception.Message);
            SetError(exception.Message);
            return null;
        }
    }

    /// <summary>轮换只换明文，不改能力。</summary>
    public McpCreatedAccessKey? Rotate()
    {
        if (_original is null || _store is null)
        {
            return null;
        }

        try
        {
            var rotated = _store.Rotate(_original.Id);
            if (rotated is not null)
            {
                CreatedToken = rotated.RawToken;
            }

            return rotated;
        }
        catch (Exception exception)
        {
            AppServices.Logger.Warn("轮换 MCP 访问密钥失败：" + exception.Message);
            SetError(exception.Message);
            return null;
        }
    }

    public bool Delete()
    {
        if (_original is null || _store is null)
        {
            return true;
        }

        try
        {
            return _store.Delete(_original.Id);
        }
        catch (Exception exception)
        {
            AppServices.Logger.Warn("删除 MCP 访问密钥失败：" + exception.Message);
            SetError(exception.Message);
            return false;
        }
    }

    private bool IsSelected(string capability) => capability switch
    {
        McpCapabilityCatalog.GamesRead => GamesReadEnabled,
        McpCapabilityCatalog.UploadRead => UploadReadEnabled,
        McpCapabilityCatalog.UploadWrite => UploadWriteEnabled,
        McpCapabilityCatalog.PlaySetsRead => PlaySetsReadEnabled,
        McpCapabilityCatalog.PlaySetsPlan => PlaySetsPlanEnabled,
        McpCapabilityCatalog.SettingsRead => SettingsReadEnabled,
        _ => SettingsPlanEnabled,
    };

    private void ApplyCapabilities(IReadOnlyList<string> capabilities)
    {
        var set = new HashSet<string>(capabilities, StringComparer.Ordinal);
        GamesReadEnabled = set.Contains(McpCapabilityCatalog.GamesRead);
        UploadReadEnabled = set.Contains(McpCapabilityCatalog.UploadRead);
        UploadWriteEnabled = set.Contains(McpCapabilityCatalog.UploadWrite);
        PlaySetsReadEnabled = set.Contains(McpCapabilityCatalog.PlaySetsRead);
        PlaySetsPlanEnabled = set.Contains(McpCapabilityCatalog.PlaySetsPlan);
        SettingsReadEnabled = set.Contains(McpCapabilityCatalog.SettingsRead);
        SettingsPlanEnabled = set.Contains(McpCapabilityCatalog.SettingsPlan);
    }

    private static (string Title, string Description) KeysFor(string capability) => capability switch
    {
        McpCapabilityCatalog.GamesRead => (Keys.SettingsMcpCapGames, Keys.SettingsMcpCapGamesDesc),
        McpCapabilityCatalog.UploadRead => (Keys.SettingsMcpCapUploadRead, Keys.SettingsMcpCapUploadReadDesc),
        McpCapabilityCatalog.UploadWrite => (Keys.SettingsMcpCapUploadWrite, Keys.SettingsMcpCapUploadWriteDesc),
        McpCapabilityCatalog.PlaySetsRead => (Keys.SettingsMcpCapPlaySetsRead, Keys.SettingsMcpCapPlaySetsReadDesc),
        McpCapabilityCatalog.PlaySetsPlan => (Keys.SettingsMcpCapPlaySetsPlan, Keys.SettingsMcpCapPlaySetsPlanDesc),
        McpCapabilityCatalog.SettingsRead => (Keys.SettingsMcpCapSettingsRead, Keys.SettingsMcpCapSettingsReadDesc),
        _ => (Keys.SettingsMcpCapSettingsPlan, Keys.SettingsMcpCapSettingsPlanDesc),
    };

    private void SetError(string message)
    {
        _errorMessage = message;
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
    }
}
