namespace PdxLauncher.Core.ModStudio;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 新建 mod 时写入的默认封面。
/// <para>
/// 早期实现写一张 1×1 透明 PNG，Steam 侧看起来就是「没有封面」；这里改为内嵌启动器自带的
/// 150×150 图标，让新建的 mod 一上传就有可用预览图。
/// </para>
/// </summary>
public static class ModCoverAssets
{
    /// <summary>内嵌资源名，必须与 csproj 里的 LogicalName 一致。</summary>
    public const string LogicalName = "PdxLauncher.ModStudio.default-cover.png";

    /// <summary>资源缺失时的兜底 1×1 PNG（只在嵌入出问题时用到）。</summary>
    private static readonly byte[] FallbackPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static readonly Lazy<byte[]> LazyDefault = new(Load);

    /// <summary>默认封面的 PNG 字节。</summary>
    public static byte[] Default => LazyDefault.Value;

    /// <summary>把默认封面写到目标文件；写入失败返回 false（不抛异常）。</summary>
    public static bool WriteDefault(IFileSystem fileSystem, string destination)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        try
        {
            fileSystem.EnsureDirectory(Path.GetDirectoryName(destination) ?? destination);
            fileSystem.WriteAllBytesAtomic(destination, Default);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] Load()
    {
        try
        {
            using var stream = typeof(ModCoverAssets).Assembly.GetManifestResourceStream(LogicalName);
            if (stream is null)
            {
                return FallbackPng;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            return bytes.Length > 128 ? bytes : FallbackPng;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return FallbackPng;
        }
    }
}
