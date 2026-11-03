namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 术语词典条目：源文词条 → 目标文词条。
/// <para>
/// 本地引擎用它纠正两类问题：一是作品里到处出现的通用词（mod、workshop 等），
/// 二是 Paradox 各作专有的机制、地名、派系名——NMT 不会自动认这些词，
/// 必须显式给出对照表才会稳定译对。
/// </para>
/// </summary>
/// <param name="Source">源语言词条；按大小写不敏感匹配。</param>
/// <param name="Target">目标语言译名。</param>
public sealed record LocalGlossaryEntry(string Source, string Target);
