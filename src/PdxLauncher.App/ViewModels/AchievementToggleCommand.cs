namespace PdxLauncher.App.ViewModels;

using System.Windows.Input;

/// <summary>
/// 行内展开按钮的命令。只持有一个回调、没有 async 语义，也不依赖 WinUI；
/// 自检工程链接行 VM 时可以原样编译。
/// </summary>
public sealed class AchievementToggleCommand(Action execute) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => true;

    /// <inheritdoc />
    public void Execute(object? parameter) => execute();

    /// <summary>命令永远可执行，不需要外部失效通知。</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
