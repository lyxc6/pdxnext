namespace PdxLauncher.Checks;

using System.Globalization;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 应用图标自检。
/// 图标是「生成物 + 两处接入点」，任何一处断开都不会让程序报错，只会让 exe/窗口悄悄退回
/// .NET 默认图标或者 WinUI 模板占位图——所以这里把 ICO 容器结构、每帧的内容、8 张同名
/// 模板素材、以及 csproj/窗口代码里的两个接入点一起钉死。
/// </summary>
public static class IconChecks
{
    /// <summary>ICO 里必须有的帧；小尺寸单独绘制（不是从 256 缩下去），所以 16/20/24 也在其中。</summary>
    private static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    /// <summary>WinUI 模板占位图标的 MD5：退回占位图时容器结构照样合法，只能靠指纹认出来。</summary>
    private const string PlaceholderMd5 = "c2838761752b2161371390e41154ed92";

    /// <summary>Assets 里沿用 MS 模板文件名的方形素材（尺寸取自模板本身，不会因为换图而变）。</summary>
    private static readonly (string Name, int Width, int Height)[] SquareAssets =
    {
        ("Square44x44Logo.scale-200.png", 88, 88),
        ("Square150x150Logo.scale-200.png", 300, 300),
        ("StoreLogo.png", 50, 50),
        ("LockScreenLogo.scale-200.png", 48, 48),
        ("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24),
        ("Square44x44Logo.targetsize-48_altform-lightunplated.png", 48, 48),
    };

    /// <summary>横幅类素材：图标居中摆在底色上。</summary>
    private static readonly (string Name, int Width, int Height)[] WideAssets =
    {
        ("SplashScreen.scale-200.png", 1240, 600),
        ("Wide310x150Logo.scale-200.png", 620, 300),
    };

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var appDir = Path.Combine(root, "src", "PdxLauncher.App");
        var assetsDir = Path.Combine(appDir, "Assets");
        var icoPath = Path.Combine(assetsDir, "AppIcon.ico");

        if (!ChecksContext.Check("Assets\\AppIcon.ico 存在", File.Exists(icoPath), icoPath))
        {
            return ChecksContext.Failures - before;
        }

        var ico = File.ReadAllBytes(icoPath);
        CheckContainer(ico);
        CheckFrames(ico);
        CheckAssets(assetsDir);
        CheckWiring(appDir, root);

        return ChecksContext.Failures - before;
    }

    /// <summary>ICO 容器的头三条：保留区、类型、帧数。</summary>
    private static void CheckContainer(byte[] data)
    {
        var count = data.Length >= 6 ? BitConverter.ToUInt16(data, 4) : -1;

        ChecksContext.Check("ICO 保留区为 0", data.Length >= 4 && BitConverter.ToUInt16(data, 0) == 0);
        ChecksContext.Check("ICO 类型为 1（图标）", data.Length >= 6 && BitConverter.ToUInt16(data, 2) == 1);
        ChecksContext.Equal("ICO 帧数", IcoSizes.Length, (int)count);
        ChecksContext.Check(
            "ICO 不是模板占位图",
            ChecksContext.Md5(data) != PlaceholderMd5,
            ChecksContext.Md5(data));
    }

    /// <summary>每一帧：尺寸集合、位深、偏移不越界也不重叠、<128 走 BMP 且 >=128 走 PNG。</summary>
    private static void CheckFrames(byte[] data)
    {
        var frames = ReadDirectory(data);
        if (!ChecksContext.Check("ICO 目录可解析", frames is not null) || frames is null)
        {
            return;
        }

        ChecksContext.Check(
            "ICO 尺寸集合为 16/20/24/32/40/48/64/128/256",
            frames.Select(f => f.Width).SequenceEqual(IcoSizes) && frames.All(f => f.Height == f.Width),
            string.Join("/", frames.Select(f => f.Width)));

        var headerEnd = 6 + (frames.Count * 16);
        var offsetsOk = true;
        var bitDepthOk = true;
        var overlapping = false;
        long previousEnd = headerEnd;
        foreach (var frame in frames)
        {
            bitDepthOk &= frame.Planes == 1 && frame.BitCount == 32;
            offsetsOk &= frame.Size > 0
                && frame.Offset >= headerEnd
                && (long)frame.Offset + frame.Size <= data.Length;
            overlapping |= frame.Offset < previousEnd;
            previousEnd = (long)frame.Offset + frame.Size;
        }

        ChecksContext.Check("ICO 每帧都是 32 位", bitDepthOk);
        ChecksContext.Check("ICO 帧偏移在文件范围内", offsetsOk);
        ChecksContext.Check("ICO 帧不重叠且按顺序排列", !overlapping);
        ChecksContext.Check(
            "ICO 文件大小与帧偏移自洽",
            previousEnd == data.Length,
            previousEnd + "B / 文件 " + data.Length + "B");

        // 小尺寸用 BMP（老 shell 不认 PNG 帧），128 以上用 PNG（体积小）。
        var small = frames.Where(f => f.Width < 128).ToList();
        var large = frames.Where(f => f.Width >= 128).ToList();
        ChecksContext.Check(
            "小尺寸帧为 32 位 BMP",
            small.Count > 0 && small.All(f => BitConverter.ToUInt32(data, (int)f.Offset) == 40));
        ChecksContext.Check(
            "大尺寸帧为 PNG",
            large.Count > 0 && large.All(f => IsPng(data.AsSpan((int)f.Offset))));

        // 32px 那帧用来验证「画的是金色字标、四角是透明圆角」，而不只是容器合法。
        var frame32 = frames.FirstOrDefault(f => f.Width == 32);
        if (frame32.Width != 32 || frame32.Size == 0)
        {
            ChecksContext.Check("ICO 含 32×32 帧", false);
            return;
        }

        var gold = GoldRatio(data, frame32);
        ChecksContext.Check("32px 帧里有金色字标", gold > 0.08 && gold < 0.60, "金色占比 " + RatioText(gold));
        ChecksContext.Check("32px 帧四角透明（圆角底）", PixelAlpha(data, frame32, 0, 0) == 0 && PixelAlpha(data, frame32, 31, 31) == 0);
        ChecksContext.Check("32px 帧中心不透明", PixelAlpha(data, frame32, 16, 16) >= 250);
    }

    /// <summary>8 张沿用模板文件名的 PNG：存在、是 PNG、像素尺寸与模板一致。</summary>
    private static void CheckAssets(string assetsDir)
    {
        foreach (var (name, width, height) in SquareAssets.Concat(WideAssets))
        {
            var path = Path.Combine(assetsDir, name);
            if (!ChecksContext.Check("Assets\\" + name + " 存在", File.Exists(path), path))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            ChecksContext.Check("Assets\\" + name + " 是 PNG", IsPng(bytes), bytes.Length + "B");
            var size = ReadPngSize(bytes);
            ChecksContext.Check(
                "Assets\\" + name + " 尺寸为 " + width + "×" + height,
                size == (width, height),
                size is null ? "无法解析 IHDR" : size.Value.Width + "×" + size.Value.Height);
        }
    }

    /// <summary>两处接入点：exe 的 ApplicationIcon、窗口的 AppWindow.SetIcon。</summary>
    private static void CheckWiring(string appDir, string root)
    {
        var csproj = Path.Combine(appDir, "PdxLauncher.App.csproj");
        if (ChecksContext.Check("PdxLauncher.App.csproj 存在", File.Exists(csproj), csproj))
        {
            var text = File.ReadAllText(csproj);
            ChecksContext.Check(
                "csproj 声明了 ApplicationIcon",
                text.Contains("<ApplicationIcon>Assets\\AppIcon.ico</ApplicationIcon>", StringComparison.Ordinal),
                "Assets\\AppIcon.ico");
        }

        var window = Path.Combine(appDir, "MainWindow.xaml.cs");
        if (ChecksContext.Check("MainWindow.xaml.cs 存在", File.Exists(window), window))
        {
            var text = File.ReadAllText(window);
            ChecksContext.Check(
                "窗口用 AppWindow.SetIcon 套图标",
                text.Contains("AppWindow.SetIcon(", StringComparison.Ordinal));
            ChecksContext.Check(
                "窗口图标取自 Assets\\AppIcon.ico",
                text.Contains("\"AppIcon.ico\"", StringComparison.Ordinal),
                "运行期取的就是 exe 资源里的同一个 ico");
        }

        // publish.cmd 会检查这个文件名，改名就等于让发布自检失效。
        var publish = Path.Combine(root, "publish.cmd");
        if (ChecksContext.Check("publish.cmd 存在", File.Exists(publish), publish))
        {
            var text = File.ReadAllText(publish);
            ChecksContext.Check(
                "publish.cmd 仍在校验 Assets\\AppIcon.ico",
                text.Contains("Assets\\AppIcon.ico", StringComparison.Ordinal));
        }
    }

    /// <summary>32 位 BMP 帧里「金色字标」占整帧的比例（自下而上存放，逐行翻转）。</summary>
    private static double GoldRatio(byte[] data, IconFrame frame)
    {
        var stride = frame.Width * 4;
        var top = (int)frame.Offset + 40;
        var gold = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            var row = top + ((frame.Height - 1 - y) * stride);
            for (var x = 0; x < frame.Width; x++)
            {
                var p = row + (x * 4);
                if (data[p + 3] < 200)
                {
                    continue;
                }

                // BGRA：金色的红/绿通道高、蓝通道低；底板与描边是中性灰，不算。
                if (data[p + 2] > 170 && data[p + 1] > 130 && data[p] < 160)
                {
                    gold++;
                }
            }
        }

        return (double)gold / (frame.Width * frame.Height);
    }

    private static int PixelAlpha(byte[] data, IconFrame frame, int x, int y)
    {
        var stride = frame.Width * 4;
        var row = (int)frame.Offset + 40 + ((frame.Height - 1 - y) * stride);
        return data[row + (x * 4) + 3];
    }

    private static List<IconFrame>? ReadDirectory(byte[] data)
    {
        if (data.Length < 6)
        {
            return null;
        }

        var count = BitConverter.ToUInt16(data, 4);
        if (count == 0 || 6 + (count * 16) > data.Length)
        {
            return null;
        }

        var frames = new List<IconFrame>(count);
        for (var i = 0; i < count; i++)
        {
            var p = 6 + (i * 16);
            frames.Add(new IconFrame(
                data[p] == 0 ? 256 : data[p],
                data[p + 1] == 0 ? 256 : data[p + 1],
                BitConverter.ToUInt16(data, p + 4),
                BitConverter.ToUInt16(data, p + 6),
                BitConverter.ToUInt32(data, p + 8),
                BitConverter.ToUInt32(data, p + 12)));
        }

        return frames;
    }

    private static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8
        && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
        && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    /// <summary>PNG 的 IHDR 紧跟 8 字节签名 + 4 字节长度 + 4 字节类型，宽高各 4 字节大端。</summary>
    private static (int Width, int Height)? ReadPngSize(byte[] data)
    {
        if (data.Length < 24 || !IsPng(data))
        {
            return null;
        }

        return (ReadInt32BigEndian(data, 16), ReadInt32BigEndian(data, 20));
    }

    private static int ReadInt32BigEndian(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static string RatioText(double value) => value.ToString("P1", CultureInfo.InvariantCulture);

    /// <summary>自检程序在 bin 下运行，向上找到带解决方案文件的仓库根。</summary>
    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private readonly record struct IconFrame(int Width, int Height, int Planes, int BitCount, uint Size, uint Offset);
}
