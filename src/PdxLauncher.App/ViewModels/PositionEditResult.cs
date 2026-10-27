namespace PdxLauncher.App.ViewModels;

/// <summary>
/// 「把某行移到第 N 位」的结果。界面据它决定是退出编辑态、保持编辑态，还是只是不动。
/// 纯枚举，不引用任何 Microsoft.UI.Xaml 类型。
/// </summary>
public enum PositionEditResult
{
    /// <summary>已重排并请求自动保存，界面应退出编辑态。</summary>
    Applied,

    /// <summary>目标位就是当前位，什么都没改，界面应退出编辑态。</summary>
    Unchanged,

    /// <summary>输入非法或越界，状态栏已给提示，界面应保持编辑态让用户改。</summary>
    Invalid,

    /// <summary>搜索过滤态不允许改顺序，状态栏已给提示，界面应退出编辑态。</summary>
    Blocked,
}
