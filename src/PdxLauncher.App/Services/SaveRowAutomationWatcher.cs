using System.ComponentModel;
using PdxLauncher.App.ViewModels;

namespace PdxLauncher.App.Services;

/// <summary>
/// 把行视图模型的读屏名变化同步到对应的列表容器。
/// 容器准备时只写得到骨架名；第二段解析完成后 <see cref="SaveGameRowViewModel.AutomationName"/>
/// 会变，这里订阅 <see cref="INotifyPropertyChanged"/> 重新写入，避免读屏名永远停在骨架态。
/// </summary>
public sealed class SaveRowAutomationWatcher
{
    private readonly Dictionary<SaveGameRowViewModel, PropertyChangedEventHandler> _handlers = [];

    /// <summary>当前仍被跟踪的行数，用于自检确认回收时没有泄漏。</summary>
    public int Count => _handlers.Count;

    /// <summary>
    /// 开始跟踪一行，并立即写入一次当前读屏名。
    /// 同一行重复跟踪会先解除旧订阅。
    /// </summary>
    public void Track(SaveGameRowViewModel row, Action<string> applyName)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(applyName);

        Untrack(row);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SaveGameRowViewModel.AutomationName))
            {
                applyName(row.AutomationName);
            }
        };

        row.PropertyChanged += handler;
        _handlers.Add(row, handler);
        applyName(row.AutomationName);
    }

    /// <summary>停止跟踪一行；未跟踪时返回 false。</summary>
    public bool Untrack(SaveGameRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!_handlers.Remove(row, out var handler))
        {
            return false;
        }

        row.PropertyChanged -= handler;
        return true;
    }

    /// <summary>解除全部订阅，页面卸载或整表重建时调用。</summary>
    public void Clear()
    {
        foreach (var (row, handler) in _handlers)
        {
            row.PropertyChanged -= handler;
        }

        _handlers.Clear();
    }
}
