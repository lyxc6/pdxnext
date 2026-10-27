namespace PdxLauncher.Core.Parsing;

/// <summary>
/// Valve KeyValues（VDF / ACF）节点树。块节点的 <see cref="Value"/> 为 null 且 <see cref="Children"/> 非空；
/// 标量节点没有子节点。<c>libraryfolders.vdf</c> 与 <c>appmanifest_*.acf</c> 共用这棵树。
/// </summary>
public sealed class ValveKeyValuesNode
{
    private readonly List<ValveKeyValuesNode> _children;

    /// <summary>构造一个节点。</summary>
    /// <param name="key">键名（根节点为空字符串）。</param>
    /// <param name="value">标量值；块节点传 null。</param>
    /// <param name="children">子节点；标量节点可传 null。</param>
    public ValveKeyValuesNode(string key, string? value, IEnumerable<ValveKeyValuesNode>? children = null)
    {
        Key = key ?? string.Empty;
        Value = value;
        _children = children is null
            ? new List<ValveKeyValuesNode>()
            : new List<ValveKeyValuesNode>(children);
    }

    /// <summary>键名，保留原始大小写。</summary>
    public string Key { get; }

    /// <summary>标量值；块节点为 null。</summary>
    public string? Value { get; }

    /// <summary>是否为块节点（<c>"key" { ... }</c>）。</summary>
    public bool IsBlock => Value is null;

    /// <summary>直接子节点，保持文件中的顺序。</summary>
    public IReadOnlyList<ValveKeyValuesNode> Children => _children;

    /// <summary>按名称查找第一个直接子节点（大小写不敏感）。</summary>
    public ValveKeyValuesNode? FindChild(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _children.FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>取直接子节点的标量值；不存在或为块节点时返回 null。</summary>
    public string? GetValue(string key) => FindChild(key)?.Value;

    /// <summary>递归查找所有同名节点（大小写不敏感），按深度优先顺序返回。</summary>
    public IEnumerable<ValveKeyValuesNode> FindAll(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (var child in _children)
        {
            if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }

            foreach (var nested in child.FindAll(key))
            {
                yield return nested;
            }
        }
    }

    /// <summary>深度优先枚举所有后代节点（不含自身）。</summary>
    public IEnumerable<ValveKeyValuesNode> Descendants()
    {
        foreach (var child in _children)
        {
            yield return child;
            foreach (var nested in child.Descendants())
            {
                yield return nested;
            }
        }
    }

    /// <summary>追加一个子节点（仅供同程序集的解析器使用）。</summary>
    internal void AddChild(ValveKeyValuesNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }

    /// <inheritdoc />
    public override string ToString() =>
        IsBlock ? Key + " { " + _children.Count + " }" : Key + " = " + Value;
}
