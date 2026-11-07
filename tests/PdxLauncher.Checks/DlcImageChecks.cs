namespace PdxLauncher.Checks;

using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// DLC 行图片显示口径的自检：本地缩略图 / Steam 宽幅头图谁优先、什么时候用 Uniform 完整显示。
/// <para>
/// 背景：EU5 的 DLC 缩略图是 500×500 方形，卡片图片框是 208×117（16:9）。
/// 以前一律 <c>UniformToFill</c>，方图上下各被裁约 22%，盾徽/建筑只剩中间一条，用户看到的就是「图片歪了」。
/// 现在的口径：EU5 优先商店宽幅头图；本地方形图只作离线兜底，并用 Uniform 完整显示。
/// 判据错了不会崩，只在真机上表现为「图被裁掉」或「有本地图还去联网」，编译器看不见，
/// 所以这里用真实的 <see cref="DlcRowViewModel"/> 直接钉住。
/// </para>
/// </summary>
public static class DlcImageChecks
{
    private const string LocalThumbnail = @"F:\SteamLibrary\steamapps\common\Europa Universalis V\game\dlc\D008\thumbnail.dds";
    private const string SteamArtwork = @"G:\PdxNext\data\dlc-artwork\3699010.jpg";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckEu5PrefersSteamArtwork();
        CheckNormalGameKeepsLocalThumbnail();
        CheckUnownedRowUsesSteamArtwork();
        CheckEu5WithoutSteamIdKeepsLocal();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ EU5：商店宽幅图优先，本地方图兜底

    private static void CheckEu5PrefersSteamArtwork()
    {
        var row = new DlcRowViewModel(
            Info("Fate of the Phoenix", "3699010", LocalThumbnail),
            isEnabled: true,
            canBeDisabled: true,
            preferSteamArtwork: true,
            hasSquareThumbnail: true);

        ChecksContext.Check("DLC 图片: EU5 取到商店图前用本地方图", row.ImagePath == LocalThumbnail, row.ImagePath);
        ChecksContext.Check("DLC 图片: EU5 方图不填满 16:9（完整显示）", !row.FillsImageBox);
        ChecksContext.Check("DLC 图片: EU5 有本地缩略图也要商店图", row.NeedsSteamArtwork);

        var changed = new List<string>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);
        row.SetArtworkPath(SteamArtwork);

        ChecksContext.Check("DLC 图片: EU5 商店图回填后改用商店图", row.ImagePath == SteamArtwork, row.ImagePath);
        ChecksContext.Check("DLC 图片: EU5 商店图仍完整显示", !row.FillsImageBox);
        ChecksContext.Check("DLC 图片: EU5 取到后不再重复请求", !row.NeedsSteamArtwork);
        ChecksContext.Check(
            "DLC 图片: EU5 切换图片时通知 Stretch 重算",
            changed.Contains(nameof(DlcRowViewModel.FillsImageBox)),
            string.Join(",", changed));
    }

    // ------------------------------------------------------------ 其他游戏：本地横图继续优先并填满

    private static void CheckNormalGameKeepsLocalThumbnail()
    {
        var row = new DlcRowViewModel(
            Info("Royal Court", "1158310", LocalThumbnail),
            isEnabled: true,
            canBeDisabled: true);

        ChecksContext.Check("DLC 图片: 普通游戏本地横图填满 16:9", row.FillsImageBox);
        ChecksContext.Check("DLC 图片: 普通游戏有本地图不取商店图", !row.NeedsSteamArtwork);

        row.SetArtworkPath(SteamArtwork);
        ChecksContext.Check("DLC 图片: 普通游戏商店图不覆盖本地缩略图", row.ImagePath == LocalThumbnail, row.ImagePath);
    }

    // ------------------------------------------------------------ 未拥有行：没有本地图，只能取商店图

    private static void CheckUnownedRowUsesSteamArtwork()
    {
        var row = new DlcRowViewModel(
            Info("Ancient Monuments", "4091650", string.Empty),
            isEnabled: true,
            canBeDisabled: false,
            showsToggle: false);

        ChecksContext.Check("DLC 图片: 未拥有行需要商店图", row.NeedsSteamArtwork);
        row.SetArtworkPath(SteamArtwork);
        ChecksContext.Check("DLC 图片: 未拥有行商店图生效", row.ImagePath == SteamArtwork, row.ImagePath);
        ChecksContext.Check("DLC 图片: 未拥有行宽幅图完整显示", !row.FillsImageBox);
    }

    // ------------------------------------------------------------ EU5 无 appid：不联网，本地方图完整显示

    private static void CheckEu5WithoutSteamIdKeepsLocal()
    {
        var row = new DlcRowViewModel(
            Info("Monumental Bonus", string.Empty, LocalThumbnail),
            isEnabled: true,
            canBeDisabled: true,
            preferSteamArtwork: true,
            hasSquareThumbnail: true);

        ChecksContext.Check("DLC 图片: EU5 无 appid 不请求商店图", !row.NeedsSteamArtwork);
        ChecksContext.Check(
            "DLC 图片: EU5 无 appid 本地方图完整显示",
            row.ImagePath == LocalThumbnail && !row.FillsImageBox,
            row.ImagePath);
    }

    private static DlcInfo Info(string displayName, string steamId, string thumbnailPath) =>
        new(
            displayName,
            PopsId: displayName.ToLowerInvariant(),
            SteamId: steamId,
            FilePathRelativeToDlcPath: displayName + ".dlc.json",
            ThumbnailPath: thumbnailPath,
            Description: string.Empty,
            RawBytes: []);
}
