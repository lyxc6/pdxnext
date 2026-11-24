namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

/// <summary>页面内容区右键刷新的命中规则：上下文菜单和文本输入控件优先。</summary>
public static class PageRefreshGesture
{
    public static bool ShouldRefresh(object? originalSource, UIElement root)
    {
        if (originalSource is not DependencyObject source)
        {
            return false;
        }

        for (DependencyObject? current = source; current is not null; current = GetParent(current))
        {
            if (current is TextBox or AutoSuggestBox or PasswordBox or RichEditBox)
            {
                return false;
            }

            if (current is FrameworkElement element && element.ContextFlyout is not null)
            {
                return false;
            }

            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject element) => element switch
    {
        FrameworkElement frameworkElement => frameworkElement.Parent,
        _ => null,
    };
}
