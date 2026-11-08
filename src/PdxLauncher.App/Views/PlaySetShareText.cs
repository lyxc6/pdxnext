namespace PdxLauncher.App.Views;

using PdxLauncher.App.Localization;

/// <summary>
/// 播放集导入 / 导出失败码 → 界面文案。
/// <para>
/// Core 只返回稳定错误码（<c>share-wrong-game:ck3</c> 这类），中文在这里拼，
/// 与 <see cref="Core.Contracts.AppResult{T}"/> 的约定一致：Core 不写中文字面量。
/// </para>
/// </summary>
internal static class PlaySetShareText
{
    public static string DescribeError(Localizer localizer, string? errorCode)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return localizer.ModsShareErrUnknown;
        }

        var separator = errorCode.IndexOf(':');
        var code = separator < 0 ? errorCode : errorCode[..separator];
        var detail = separator < 0 ? string.Empty : errorCode[(separator + 1)..];

        return code switch
        {
            "share-invalid-json" => localizer.ModsImportErrInvalidJson,
            "share-missing-field" => localizer.T(Keys.ModsImportErrMissingField, detail),
            "share-invalid-mod" => localizer.T(Keys.ModsImportErrInvalidMod, detail),
            "share-wrong-game" => localizer.T(Keys.ModsImportErrWrongGame, detail),
            "share-duplicate-mods" => localizer.T(Keys.ModsImportErrDuplicate, detail),
            "share-file-not-found" => localizer.T(Keys.ModsImportErrFileNotFound, detail),
            "share-no-game" => localizer.ModsImportErrNoGame,
            "share-no-selection" => localizer.ModsImportErrNoSelection,
            "share-path-empty" => localizer.ModsImportErrNoSelection,
            "playSet-not-found" => localizer.T(Keys.ModsShareErrUnknownDetail, detail),
            "share-create-failed" => localizer.T(Keys.ModsShareErrUnknownDetail, code),
            "share-save-failed" => localizer.T(Keys.ModsShareErrUnknownDetail, code),
            // 其余（含 IO 异常原文）直接展示，至少不会把「真实原因」吞掉。
            _ => errorCode,
        };
    }
}
