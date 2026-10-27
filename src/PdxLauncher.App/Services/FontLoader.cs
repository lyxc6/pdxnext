namespace PdxLauncher.App.Services;

using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 从 TTF/OTF 文件加载字体：解析 name 表取族名，再用 AddFontResourceEx(FR_PRIVATE)
/// 把字体注册进当前进程，最后构造 <see cref="FontFamily"/>。
/// 任何一步失败都退回系统默认字体，绝不抛异常。
/// </summary>
public sealed class FontLoader
{
    private const int FrPrivate = 0x10;
    private const ushort NameIdFamily = 1;
    private const ushort NameIdTypographicFamily = 16;
    private const ushort PlatformIdWindows = 3;

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly Dictionary<string, FontFamily?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FontLoader(IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>字体文件路径 → FontFamily；失败返回 null（调用方用默认字体）。</summary>
    public FontFamily? Load(string? fontPath)
    {
        if (string.IsNullOrWhiteSpace(fontPath))
        {
            return null;
        }

        if (_cache.TryGetValue(fontPath, out var cached))
        {
            return cached;
        }

        FontFamily? result = null;
        try
        {
            if (!_fileSystem.FileExists(fontPath))
            {
                _logger.Warn("字体文件不存在: " + fontPath);
            }
            else
            {
                var familyName = TryReadFamilyName(_fileSystem.ReadAllBytes(fontPath));
                if (string.IsNullOrWhiteSpace(familyName))
                {
                    _logger.Warn("无法从字体文件读取族名: " + fontPath);
                }
                else
                {
                    AddFontResourceEx(fontPath, FrPrivate, IntPtr.Zero);
                    result = new FontFamily(familyName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
        {
            _logger.Warn("字体加载失败: " + fontPath + " (" + ex.Message + ")");
            result = null;
        }

        _cache[fontPath] = result;
        return result;
    }

    private static string? TryReadFamilyName(byte[] data)
    {
        if (data.Length < 12)
        {
            return null;
        }

        var tag = ReadUInt32BigEndian(data, 0);
        if (tag != 0x00010000 && tag != 0x4F54544F && tag != 0x74727565)
        {
            // 0x00010000 = TrueType, OTTO = CFF/OpenType, 'true' = 老 TrueType
            return null;
        }

        var tableCount = ReadUInt16BigEndian(data, 4);
        var nameOffset = -1;
        var nameLength = 0;
        for (var i = 0; i < tableCount; i++)
        {
            var record = 12 + (i * 16);
            if (record + 16 > data.Length)
            {
                return null;
            }

            if (ReadUInt32BigEndian(data, record) == 0x6E616D65)
            {
                nameOffset = (int)ReadUInt32BigEndian(data, record + 8);
                nameLength = (int)ReadUInt32BigEndian(data, record + 12);
                break;
            }
        }

        if (nameOffset < 0 || nameOffset + 6 > data.Length)
        {
            return null;
        }

        var end = Math.Min(data.Length, nameOffset + nameLength);
        var count = ReadUInt16BigEndian(data, nameOffset + 2);
        var stringOffset = nameOffset + ReadUInt16BigEndian(data, nameOffset + 4);

        string? typographic = null;
        string? family = null;
        for (var i = 0; i < count; i++)
        {
            var record = nameOffset + 6 + (i * 12);
            if (record + 12 > end)
            {
                break;
            }

            var platformId = ReadUInt16BigEndian(data, record);
            var nameId = ReadUInt16BigEndian(data, record + 6);
            var length = ReadUInt16BigEndian(data, record + 8);
            var offset = ReadUInt16BigEndian(data, record + 10);
            if (platformId != PlatformIdWindows || length == 0 || stringOffset + offset + length > data.Length)
            {
                continue;
            }

            var value = ReadUtf16BigEndian(data, stringOffset + offset, length / 2);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (nameId == NameIdTypographicFamily && typographic is null)
            {
                typographic = value;
            }
            else if (nameId == NameIdFamily && family is null)
            {
                family = value;
            }
        }

        return typographic ?? family;
    }

    private static uint ReadUInt32BigEndian(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private static ushort ReadUInt16BigEndian(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static string ReadUtf16BigEndian(byte[] data, int offset, int charCount)
    {
        var chars = new char[charCount];
        for (var i = 0; i < charCount; i++)
        {
            chars[i] = (char)ReadUInt16BigEndian(data, offset + (i * 2));
        }

        return new string(chars);
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string fileName, int flags, IntPtr reserved);
}
