namespace PdxLauncher.Core.Saves;

/// <summary>单份存档的解析完成度。</summary>
public enum SaveParseState
{
    /// <summary>
    /// 至少从容器元数据里读到一个可展示字段（版本、游戏内日期、存档名、玩家、国家或铁人标记）。
    /// <para>仅由文件名推导出的日期/名称不算元数据，不改变本状态。</para>
    /// </summary>
    Ok = 0,

    /// <summary>容器损坏/截断/格式未识别，一个元数据字段都没取到，只剩文件名与文件时间可用。</summary>
    Degraded = 1,
}
