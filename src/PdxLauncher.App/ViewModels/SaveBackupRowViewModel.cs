namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Saves;

/// <summary>
/// 备份段的一行：文件名 / 备份时间 / 大小，行内提供「恢复」「删除备份」。
/// 与 <see cref="SaveGameRowViewModel"/> 一样不引用 WinUI 类型，可见性交给 XAML 的 XamlVisibility；
/// 文案全部走生成键，切语言时由基类统一重发通知。
/// </summary>
public sealed class SaveBackupRowViewModel : LocalizedObservableObject
{
    private readonly int _index;

    private bool _isBusy;

    public SaveBackupRowViewModel(SaveBackupEntry entry, int index)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        _index = index;
    }

    /// <summary>服务返回的不可变条目（恢复/删除时原样交回服务）。</summary>
    public SaveBackupEntry Entry { get; }

    public string FileName => Entry.OriginalFileName;

    /// <summary>备份正文的完整路径只作为悬停提示。</summary>
    public string FilePath => Entry.BackupPath;

    /// <summary>UI 自动化 id；保持 ASCII，不随语言变化。</summary>
    public string AutomationId => "backup_" + _index.ToString(CultureInfo.InvariantCulture);

    /// <summary>读屏名字：文件名 + 备份时间，避免整列都读成类型名。</summary>
    public string AutomationName =>
        T(Keys.SavesBackupRowName, FileName, CreatedText);

    public string CreatedText =>
        T(
            Keys.SavesBackupCreated,
            Entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

    public string SizeText => SizeFormatter.Format(Entry.SizeBytes);

    /// <summary>本行是否正在恢复/删除；期间两个按钮都禁用。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRestore));
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    public bool CanRestore => !IsBusy;

    public bool CanDelete => !IsBusy;

    /// <summary>行内按钮文案：DataTemplate 里只能绑行 VM，绑不到页面上的 <c>L</c>。</summary>
    public string RestoreText => T(Keys.SavesActionRestore);

    public string DeleteText => T(Keys.SavesActionDeleteBackup);

    /// <summary>由视图模型在操作开始时置位；结束时必须在 finally 里复位。</summary>
    internal void SetBusy(bool busy) => IsBusy = busy;
}
