namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 主页自适应的两层保证：
/// ① 纯策略 <see cref="HomeScalePolicy"/> 的缩放边界；
/// ② 接线事实——主页确实用缩放容器包住内容、确实按视口宽度重算，且最小窗口真正被锁住。
/// </summary>
public static class HomeAdaptiveChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckPolicy();
        CheckHomeWiring();
        CheckMinimumWindow();

        return ChecksContext.Failures - before;
    }

    private static void CheckPolicy()
    {
        ChecksContext.Equal(
            "内容区达到设计尺寸时 1.0",
            1.0,
            HomeScalePolicy.Resolve(HomeScalePolicy.DesignContentWidth));
        ChecksContext.Equal("内容区更宽也不放大", 1.0, HomeScalePolicy.Resolve(HomeScalePolicy.DesignContentWidth * 2));
        ChecksContext.Equal(
            "宽度少 10% 时缩到 0.9",
            0.9,
            Math.Round(HomeScalePolicy.Resolve(HomeScalePolicy.DesignContentWidth * 0.9), 3));
        ChecksContext.Equal(
            "默认窗口内容区约 0.879（三卡仍同排）",
            0.879,
            Math.Round(HomeScalePolicy.Resolve(984), 3));
        ChecksContext.Equal(
            "最小窗口内容区锁到下限 0.75",
            0.75,
            Math.Round(HomeScalePolicy.Resolve(808), 3));
        ChecksContext.Equal("内容区低于下限时锁 0.75", 0.75, HomeScalePolicy.Resolve(HomeScalePolicy.DesignContentWidth * 0.6));
        ChecksContext.Equal("0 宽按 1.0（不缩放）", 1.0, HomeScalePolicy.Resolve(0));
        ChecksContext.Equal("NaN 宽按 1.0", 1.0, HomeScalePolicy.Resolve(double.NaN));
        ChecksContext.Equal("非法设计宽按 1.0", 1.0, HomeScalePolicy.Resolve(800, double.PositiveInfinity, 0));

        ChecksContext.Equal("最小窗口 1040", 1040.0, HomeScalePolicy.MinimumWindowWidth);
        ChecksContext.Equal("导航 216", 216.0, HomeScalePolicy.NavigationPaneWidth);
        ChecksContext.Equal("下限 0.75", 0.75, HomeScalePolicy.MinimumScale);
    }

    private static void CheckHomeWiring()
    {
        var xaml = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Views", "HomePage.xaml"));
        if (!ChecksContext.Check("找到 HomePage.xaml", xaml is not null, xaml ?? "未找到"))
        {
            return;
        }

        var text = File.ReadAllText(xaml!);
        ChecksContext.Check(
            "主页内容包在 PdxScaledPresenter",
            text.Contains("<controls:PdxScaledPresenter x:Name=\"HomeScaleHost\">", StringComparison.Ordinal)
                && text.Contains("</controls:PdxScaledPresenter>", StringComparison.Ordinal));
        ChecksContext.Check(
            "视口尺寸变化触发重算",
            text.Contains("SizeChanged=\"OnHomeViewportSizeChanged\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "右键刷新仍挂在 HomeScrollViewer",
            text.Contains("RightTapped=\"OnPageRightTapped\"", StringComparison.Ordinal));

        var code = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Views", "HomePage.xaml.cs"));
        if (!ChecksContext.Check("找到 HomePage.xaml.cs", code is not null, code ?? "未找到"))
        {
            return;
        }

        var codeText = File.ReadAllText(code!);
        ChecksContext.Check(
            "用 HomeScalePolicy 计算缩放",
            codeText.Contains("HomeScalePolicy.Resolve(", StringComparison.Ordinal));
        ChecksContext.Check(
            "缩放写回 HomeScaleHost.ScaleFactor",
            codeText.Contains("HomeScaleHost.ScaleFactor = scale", StringComparison.Ordinal));
        ChecksContext.Check(
            "缩放属性名不与 UIElement.Scale 冲突",
            codeText.Contains("HomeScaleHost.ScaleFactor", StringComparison.Ordinal));
        ChecksContext.Check(
            "加载时先算一次缩放",
            codeText.Contains("ApplyHomeScale(HomeScrollViewer.ActualWidth, HomeScrollViewer.ActualHeight)", StringComparison.Ordinal));

        var presenter = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Controls", "PdxScaledPresenter.cs"));
        if (ChecksContext.Check("找到 PdxScaledPresenter.cs", presenter is not null, presenter ?? "未找到"))
        {
            var presenterText = File.ReadAllText(presenter!);
            ChecksContext.Check(
                "缩放容器按缩放后尺寸参与测量",
                presenterText.Contains("MeasureOverride", StringComparison.Ordinal)
                    && presenterText.Contains("DesiredSize.Width * scale", StringComparison.Ordinal));
            ChecksContext.Check(
                "缩放容器按缩放后尺寸参与排版",
                presenterText.Contains("ArrangeOverride", StringComparison.Ordinal)
                    && presenterText.Contains("finalSize.Width / scale", StringComparison.Ordinal));
        }
    }

    private static void CheckMinimumWindow()
    {
        var main = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "MainWindow.xaml.cs"));
        if (!ChecksContext.Check("找到 MainWindow.xaml.cs", main is not null, main ?? "未找到"))
        {
            return;
        }

        var text = File.ReadAllText(main!);
        ChecksContext.Check("最小尺寸仍是 1040×600", text.Contains("MinimumWindowWidth = 1040.0", StringComparison.Ordinal)
            && text.Contains("MinimumWindowHeight = 600.0", StringComparison.Ordinal));
        ChecksContext.Check(
            "用 WM_GETMINMAXINFO 强制最小尺寸",
            text.Contains("SetWindowSubclass(", StringComparison.Ordinal)
                && text.Contains("private void HookMinimumWindowSize()", StringComparison.Ordinal));
        ChecksContext.Check(
            "Presenter 最小尺寸按物理像素设置",
            text.Contains("presenter.PreferredMinimumWidth = (int)Math.Round(MinimumWindowWidth * scale)", StringComparison.Ordinal)
                && text.Contains("presenter.PreferredMinimumHeight = (int)Math.Round(MinimumWindowHeight * scale)", StringComparison.Ordinal));
        ChecksContext.Check(
            "WM_GETMINMAXINFO 按物理像素兜底",
            text.Contains("var minWidth = (int)Math.Round(MinimumWindowWidth * scale)", StringComparison.Ordinal)
                && text.Contains("info.MinTrackSize.X = minWidth", StringComparison.Ordinal));
        ChecksContext.Check(
            "钩子失败只警告不阻断",
            text.Contains("挂载最小窗口尺寸钩子失败", StringComparison.Ordinal));
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(dir.FullName, "version.props")))
            {
                return null;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
