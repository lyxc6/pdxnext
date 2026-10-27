namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 随程序发布的自带美术（目前只有 EU5：它的安装目录里根本没有 launcher-settings.json 与主题素材）。
/// <para>
/// 层级位置：<see cref="IThemeResolver"/> 的「层 2.5」——安装目录主题（层 2）缺失时补上，
/// 仍然让位给联网缓存（层 1）。实现必须只返回已存在文件的绝对路径，缺失文件返回 null。
/// </para>
/// </summary>
public interface IBundledThemeSource
{
    /// <summary>取得该游戏的自带主题；没有自带美术时返回 null（调用方继续用占位图）。</summary>
    ThemeSpec? TryResolve(GameInstallation game);
}
