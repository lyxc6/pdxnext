namespace PdxLauncher.App.ViewModels;

/// <summary>
/// 一行的图标呈现：本地图片路径 + 不透明度。
/// <para>
/// 不透明度只在「未解锁 + 游戏没有专门灰图」时小于 1（用压暗代替缺失的灰图）；
/// 有灰图的游戏保持 1.0，避免灰图再压一层变得看不清。
/// </para>
/// </summary>
/// <param name="Url">可直接交给 <c>BitmapImage</c> 的路径（DDS 已转成 PNG）。</param>
/// <param name="Opacity">0~1 的不透明度。</param>
public readonly record struct AchievementIconPresentation(string Url, double Opacity);
