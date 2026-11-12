namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 删除本地模型的结果三态。
/// <para>
/// 复核口径：只有「目录确实不存在」才算 <see cref="Removed"/>；目录或文件还在就是
/// <see cref="RemovedWithResidue"/>；连枚举/复核都做不了（权限、路径异常）才是 <see cref="Failed"/>。
/// </para>
/// </summary>
public enum LocalTranslationModelRemoveStatus
{
    /// <summary>删除后复核通过：模型目录已不存在。</summary>
    Removed,

    /// <summary>删掉了一部分，但目录或文件仍有残留；残留清单见结果里的 <c>Residue</c>。</summary>
    RemovedWithResidue,

    /// <summary>删除前模型目录就不存在，无需删除。</summary>
    NotFound,

    /// <summary>删除过程出现无法归类的异常，残留清单不可信。</summary>
    Failed,
}
