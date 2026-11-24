namespace PdxLauncher.App.Services;

using System.ComponentModel;
using System.Reflection;

/// <summary>
/// 把页面的忙态属性桥接到 <see cref="GlobalBusyMonitor"/>：属性变 true 时登记一条租约，
/// 变 false 时释放；页面卸载后停止监听，避免缓存页切走后还占着标题栏。
/// </summary>
public sealed class GlobalBusyBinding : IDisposable
{
    private readonly INotifyPropertyChanged _source;
    private readonly string[] _propertyNames;
    private readonly string _labelKey;
    private readonly Dictionary<string, GlobalBusyMonitor.Lease?> _leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PropertyInfo> _properties = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>创建绑定；不会立刻登记，首次状态变化或 <see cref="Refresh"/> 时才登记。</summary>
    /// <param name="source">页面 ViewModel。</param>
    /// <param name="labelKey">标题栏文案资源键。</param>
    /// <param name="propertyNames">要监听的忙态属性名，例如 <c>IsBusy</c>。</param>
    public GlobalBusyBinding(
        INotifyPropertyChanged source,
        string labelKey,
        params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(labelKey);
        ArgumentNullException.ThrowIfNull(propertyNames);
        if (propertyNames.Length == 0)
        {
            throw new ArgumentException("至少需要一个忙态属性名。", nameof(propertyNames));
        }

        _source = source;
        _labelKey = labelKey;
        _propertyNames = propertyNames;
        foreach (var name in propertyNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("忙态属性名不能为空。", nameof(propertyNames));
            }

            if (_leases.ContainsKey(name))
            {
                throw new ArgumentException("忙态属性重复：" + name, nameof(propertyNames));
            }

            var property = source.GetType().GetProperty(name);
            if (property is null || property.PropertyType != typeof(bool))
            {
                throw new ArgumentException("忙态属性必须是可读取的 bool 属性：" + name, nameof(propertyNames));
            }

            _properties[name] = property;
            _leases[name] = null;
        }

        _source.PropertyChanged += OnPropertyChanged;
    }

    /// <summary>按当前属性状态重新同步租约（页面构造完成后调用一次）。</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var name in _propertyNames)
        {
            Sync(name, ReadBusy(name));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.PropertyChanged -= OnPropertyChanged;
        foreach (var name in _propertyNames)
        {
            Release(name);
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        Sync(e.PropertyName, e.PropertyName is null or "" ? AnyBusy() : ReadBusy(e.PropertyName));

    private bool AnyBusy() => _propertyNames.Any(ReadBusy);

    private bool ReadBusy(string propertyName)
    {
        if (!_leases.ContainsKey(propertyName))
        {
            return false;
        }

        return _properties[propertyName].GetValue(_source) is true;
    }

    private void Sync(string? propertyName, bool busy)
    {
        if (_disposed || string.IsNullOrEmpty(propertyName) || !_leases.ContainsKey(propertyName))
        {
            return;
        }

        if (busy && _leases[propertyName] is null)
        {
            _leases[propertyName] = AppServices.GlobalBusy.Begin(_labelKey);
        }
        else if (!busy)
        {
            Release(propertyName);
        }
    }

    private void Release(string propertyName)
    {
        if (!_leases.TryGetValue(propertyName, out var lease))
        {
            return;
        }

        _leases[propertyName] = null;
        lease?.Dispose();
    }
}
