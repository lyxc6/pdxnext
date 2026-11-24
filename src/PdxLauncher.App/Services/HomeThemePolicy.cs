namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;

/// <summary>
/// 按游戏定制主页视觉：当前仅 Cities: Skylines 1 使用。
/// 判据不依赖任何 XAML 类型，方便自检工程直接链接并测试。
/// </summary>
internal static class HomeThemePolicy
{
    public const double DefaultLogoMaxWidth = 420;
    public const double DefaultLogoMaxHeight = 160;
    public const double CitiesSkylinesLogoMaxWidth = 320;
    public const double CitiesSkylinesLogoMaxHeight = 144;

    private const string CitiesSkylinesGameId = "cities_skylines";
    private const string CitiesSkylinesButtonForeground = "#F2FFF2";
    private const string CitiesSkylinesButtonHoverForeground = "#FFFFFF";

    public static bool IsCitiesSkylines1(string? gameId) =>
        string.Equals(
            CitiesSkylinesGameId,
            KnownGames.CanonicalizeGameId(gameId),
            StringComparison.OrdinalIgnoreCase);

    public static double LogoMaxWidth(string? gameId) =>
        IsCitiesSkylines1(gameId) ? CitiesSkylinesLogoMaxWidth : DefaultLogoMaxWidth;

    public static double LogoMaxHeight(string? gameId) =>
        IsCitiesSkylines1(gameId) ? CitiesSkylinesLogoMaxHeight : DefaultLogoMaxHeight;

    public static ThemeSpec ApplyOverrides(ThemeSpec spec, string? gameId)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return IsCitiesSkylines1(gameId)
            ? spec with
            {
                ButtonTextColor = CitiesSkylinesButtonForeground,
                ButtonHoverTextColor = CitiesSkylinesButtonHoverForeground,
            }
            : spec;
    }
}
