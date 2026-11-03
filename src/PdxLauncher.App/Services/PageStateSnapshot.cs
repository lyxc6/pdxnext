namespace PdxLauncher.App.Services;

using System.Text.Json;

/// <summary>
/// 页面状态快照的落盘形态：Mod 库页与 Mod 创建页各一份，存在自有库
/// <c>documents</c> 表的 <see cref="PdxLauncher.Core.Storage.JsonDocumentKeys.UiPageState"/> 键下。
/// <para>
/// 只存「回到页面时该恢复什么」，不存业务数据；JSON 很小（几百字节），坏掉就当没有，
/// 读失败/格式不对都返回 null，页面按首次进入处理。
/// </para>
/// </summary>
/// <param name="ModLibrary">Mod 库页状态。</param>
/// <param name="ModStudio">Mod 创建页状态。</param>
public sealed record PageStateSnapshot(ModLibraryState? ModLibrary, ModStudioState? ModStudio)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
    };

    /// <summary>序列化成一行 JSON。</summary>
    public static string Serialize(PageStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    /// <summary>反序列化；空串、坏 JSON、结构不认都返回 null。</summary>
    public static PageStateSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PageStateSnapshot>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
