namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;

/// <summary>支持等级相关的共用文案，避免多个页面各写一份而漂移。</summary>
internal static class SupportMessages
{
    /// <summary>仅启动的游戏在内容管理页（播放集 / Mod / DLC）上显示的边界说明。</summary>
    public static string ManagedByGame => Localizer.Instance.T(Keys.ShellSupportManagedByGame);
}
