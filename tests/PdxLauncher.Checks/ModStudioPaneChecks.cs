namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// Mod 创建页三处 UI 修正的源码断言（真机反馈，不是编译错误，重排一次就可能退回去）：
/// <list type="number">
/// <item>列表行徽标必须绑短文案，不能再裸绑带 <c>{0}</c> 的 <c>modstudio.rowPublished</c>。</item>
/// <item>详情面板顶部不再有「Mod 详情」小标题（下面紧跟着就是 mod 名大标题）。</item>
/// <item>上传面板必须留得出去：成功、取消、切换选中三条路径都要回详情。</item>
/// </list>
/// </summary>
public static class ModStudioPaneChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "ModStudioPage.xaml");
        var codeBehind = Read(root, "src", "PdxLauncher.App", "Views", "ModStudioPage.xaml.cs");
        var viewModel = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModStudioViewModel.cs");

        // ---- 1. 徽标文案 ----
        ChecksContext.Check(
            "Mod 创建页: 列表徽标绑短文案 PublicationBadgeText",
            xaml.Contains("{x:Bind PublicationBadgeText", StringComparison.Ordinal));
        ChecksContext.Check(
            "Mod 创建页: 列表徽标不再裸绑带占位符的 rowPublished",
            !xaml.Contains("L.ModstudioRowPublished", StringComparison.Ordinal));

        // ---- 2. 冗余标题 ----
        ChecksContext.Check(
            "Mod 创建页: 详情面板没有多余的「Mod 详情」小标题",
            !xaml.Contains("L.ModstudioDetailTitle", StringComparison.Ordinal));

        // ---- 3. 上传面板能关 ----
        ChecksContext.Check(
            "Mod 创建页: 上传成功回到详情（resetPane: true）",
            viewModel.Contains("LoadAsync(Game, cancellationToken, resetPane: true)", StringComparison.Ordinal));
        ChecksContext.Check(
            "Mod 创建页: 取消上传也回到详情",
            HasPaneResetInCancelBranch(viewModel));
        ChecksContext.Check(
            "Mod 创建页: 切换选中项时离开上传/新建面板",
            HasSelectionPaneReset(viewModel));
        ChecksContext.Check(
            "Mod 创建页: 上传中按「取消」给出「正在取消」提示",
            codeBehind.Contains("ModstudioUploadCanceling", StringComparison.Ordinal));

        // ---- 4. 详情面板的 Steam 与描述符编辑入口 ----
        var editDialog = Read(root, "src", "PdxLauncher.App", "Views", "EditModDescriptorDialog.cs");
        ChecksContext.Check(
            "Mod 创建页: 详情面板有「在 Steam 中打开」",
            xaml.Contains("modStudioOpenSteam", StringComparison.Ordinal)
                && codeBehind.Contains("SteamWorkshop.GetSteamAddress", StringComparison.Ordinal)
                && codeBehind.Contains("ShellLauncher.TryOpenSteam", StringComparison.Ordinal));
        ChecksContext.Check(
            "Mod 创建页: 详情面板有「编辑描述符」",
            xaml.Contains("modStudioEditDescriptor", StringComparison.Ordinal)
                && codeBehind.Contains("EditModDescriptorDialog.ShowAsync", StringComparison.Ordinal)
                && codeBehind.Contains("SaveDescriptorAsync", StringComparison.Ordinal));
        ChecksContext.Check(
            "Mod 创建页: 编辑入口只对有文件的描述符可用",
            viewModel.Contains("CanEditSelectedDescriptor", StringComparison.Ordinal)
                && viewModel.Contains("SaveDescriptorAsync", StringComparison.Ordinal));
        ChecksContext.Check(
            "Mod 创建页: 编辑框只提交 ModDescriptorEdit",
            editDialog.Contains("new ModDescriptorEdit(", StringComparison.Ordinal));

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static bool HasPaneResetInCancelBranch(string viewModel)
    {
        var index = viewModel.IndexOf("catch (OperationCanceledException)", StringComparison.Ordinal);
        while (index >= 0)
        {
            var end = viewModel.IndexOf('}', viewModel.IndexOf('{', index));
            if (end > index)
            {
                var body = viewModel[index..end];
                if (body.Contains("Pane = ModStudioPane.Detail", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            index = viewModel.IndexOf("catch (OperationCanceledException)", index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool HasSelectionPaneReset(string viewModel)
    {
        var index = viewModel.IndexOf("partial void OnSelectedRowChanged", StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var body = viewModel[index..Math.Min(viewModel.Length, index + 600)];
        return body.Contains("Pane != ModStudioPane.Detail", StringComparison.Ordinal)
            || body.Contains("Pane = ModStudioPane.Detail", StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts)
    {
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "i18n")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
