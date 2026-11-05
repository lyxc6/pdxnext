namespace PdxLauncher.Core.Saves;

/// <summary>单份存档的解析完成度。</summary>
public enum SaveParseState
{
    /// <summary>至少读到了版本或游戏内日期等可展示字段。</summary>
    Ok = 0,

    /// <summary>容器损坏/截断/格式未识别，只剩文件名与文件时间可用。</summary>
    Degraded = 1,
}
