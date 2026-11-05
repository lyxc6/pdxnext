namespace PdxLauncher.Core.Saves;

/// <summary>存档容器的物理形态，决定 <see cref="SavContainerReader"/> 走哪条解析路径。</summary>
public enum SaveContainer
{
    /// <summary>未识别；解析器会再按扩展名与魔数嗅探一次。</summary>
    Unknown = 0,

    /// <summary><c>SAV0</c> + 换行 + 纯文本 <c>meta_data={...}</c>（CK3）。</summary>
    TextMeta = 1,

    /// <summary><c>SAV0</c> + 换行 + Jomini 二进制 token 流（Victoria 3 / EU5）。</summary>
    BinaryToken = 2,

    /// <summary>标准 zip，只读根部的 <c>meta</c> 文本条目（Stellaris）。</summary>
    ZipMeta = 3,

    /// <summary>Colossal Order 容器（CS2 <c>.cok</c> 实为 zip；CS1 <c>.crp</c> 只保证文件名可用）。</summary>
    Colossal = 4,
}
