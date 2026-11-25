namespace PdxLauncher.Core.Localization;

/// <summary>
/// 文案键归一化。<c>Resources.resw</c> 的资源名必须是不含点的 PascalCase 标识符，
/// 因此 <c>crash.summary.gameDetail</c> 这类点分写法要先转成 <c>CrashSummaryGameDetail</c> 才能查表。
/// <para>
/// 历史遗留：崩溃分析页上线时用了点分键，而资源名是 PascalCase，整页文案在运行期全部回退成
/// <c>⟨key⟩</c>。把转换放在查表的入口，既修好字面键，也修好运行期拼出来的
/// <c>crash.finding.*.title</c> 与 <c>crash.exception.*</c>，不必再维护一张枚举到键的映射表。
/// </para>
/// </summary>
public static class LocalizationKey
{
    /// <summary>
    /// 点分键转资源名：按 <c>.</c> 分段，每段首字母大写后拼接。
    /// 已经是标识符的键原样返回，所以对结果再调一次不会变化（幂等）。
    /// </summary>
    public static string Normalize(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.IndexOf('.') < 0)
        {
            return key;
        }

        var builder = new StringBuilder(key.Length);
        foreach (var segment in key.Split('.'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(segment[0]));
            builder.Append(segment, 1, segment.Length - 1);
        }

        return builder.ToString();
    }
}
