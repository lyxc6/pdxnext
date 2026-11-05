namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 成就页的视图模型：向 <see cref="IAchievementSource"/> 取当前游戏的成就快照，
/// 把可用性翻译成明确的空状态说明，并维护「已解锁 X / Y」统计。
/// <para>
/// 不含任何 Microsoft.UI.Xaml 类型（项目 MVVM 分层硬约束）：
/// 空状态、稀有度、解锁时间都只产出 string / bool，可见性交给 XAML 的 XamlVisibility。
/// </para>
/// <para>
/// 切游戏时先清空旧列表再取数；数据源不可用时列表为空且显示原因，
/// 绝不把「没有数据源」渲染成「这款游戏没有成就」。
/// </para>
/// </summary>
public sealed class AchievementsViewModel : LocalizedObservableObject
{
    private readonly IAchievementSource _source;

    private GameInstallation? _game;
    private AchievementAvailability _availability = AchievementAvailability.NotSupported;
    private bool _isBusy;

    public AchievementsViewModel(IAchievementSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>当前展示的成就行；切游戏 / 重新取数时整体清空重建。</summary>
    public ObservableCollection<AchievementRowViewModel> Achievements { get; } = new();

    public GameInstallation? Game
    {
        get => _game;
        private set
        {
            if (SetProperty(ref _game, value))
            {
                OnPropertyChanged(nameof(GameTitle));

                // EmptyStateMessage 的「请先选游戏」分支依赖 Game：首次取数时 Game 从 null 变成实例，
                // 但 Availability 可能仍是 NotSupported（初值相同、不会触发通知），
                // 这里必须显式通知，否则页面会一直停在「请先在游戏库中选择一款游戏」。
                OnPropertyChanged(nameof(EmptyStateMessage));
            }
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.AchievementsUnavailableNoGame);

    public AchievementAvailability Availability
    {
        get => _availability;
        private set
        {
            if (SetProperty(ref _availability, value))
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(EmptyStateTitle));
                OnPropertyChanged(nameof(EmptyStateMessage));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public int TotalCount => Achievements.Count;

    public int UnlockedCount => Achievements.Count(static row => row.IsUnlocked);

    /// <summary>顶部统计文案：「已解锁 X / Y」。</summary>
    public string CountsText => T(Keys.AchievementsCounts, UnlockedCount, TotalCount);

    /// <summary>数据源可用且列表非空；为 false 时页面显示空状态说明而不是空白列表。</summary>
    public bool IsAvailable => Availability == AchievementAvailability.Available;

    public bool ShowEmptyState => !IsAvailable;

    public string EmptyStateTitle => IsAvailable ? string.Empty : T(Keys.AchievementsEmptyTitle);

    /// <summary>
    /// 空状态原因：没有当前游戏时先提示选游戏，其余按可用性给出对应文案。
    /// 三条不可用文案（未接入 / 连不上 Steam / 确实没有成就）必须各自独立，不能合并。
    /// </summary>
    public string EmptyStateMessage
    {
        get
        {
            if (Game is null)
            {
                return T(Keys.AchievementsUnavailableNoGame);
            }

            return Availability switch
            {
                AchievementAvailability.NotSupported => T(Keys.AchievementsUnavailableNotSupported),
                AchievementAvailability.SteamUnavailable => T(Keys.AchievementsUnavailableSteamUnavailable),
                AchievementAvailability.Empty => T(Keys.AchievementsUnavailableEmpty),
                _ => string.Empty,
            };
        }
    }

    /// <summary>
    /// 取当前游戏的成就。切游戏时旧列表立即清空，避免新游戏页面上残留上一款的成就。
    /// </summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;

        Achievements.Clear();
        NotifyCountsChanged();

        if (game is null)
        {
            Availability = AchievementAvailability.NotSupported;
            return;
        }

        IsBusy = true;
        try
        {
            var snapshot = await _source.GetAsync(game.GameId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Apply(snapshot);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(AchievementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var entries = snapshot.Entries ?? [];
        foreach (var entry in entries)
        {
            Achievements.Add(new AchievementRowViewModel(entry));
        }

        // 数据源说「可用」但一条都没有：按 Empty 解释，页面才有话说，而不是留一块空白。
        Availability = snapshot.Availability == AchievementAvailability.Available && entries.Count == 0
            ? AchievementAvailability.Empty
            : snapshot.Availability;

        NotifyCountsChanged();
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(CountsText));
    }
}
