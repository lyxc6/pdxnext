namespace PdxLauncher.App.Services;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

/// <summary>
/// 支持整体替换的 <see cref="ObservableCollection{T}"/>。
/// <para>
/// 逐条 <c>Clear</c> + <c>Add</c> 会让绑定到 ItemsSource 的 ListView 为每一行都做一次
/// 集合变更通知；30 行实测能在 UI 线程上量出几百毫秒的布局停顿。这里把「清空 + 装满」
/// 合成一次 <see cref="NotifyCollectionChangedAction.Reset"/>，让列表只重建一次视图。
/// </para>
/// </summary>
/// <typeparam name="T">集合元素类型。</typeparam>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>用一批新元素整体替换当前内容；只触发一次 Reset。</summary>
    /// <param name="items">新内容；枚举期间不能被外部修改。</param>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
