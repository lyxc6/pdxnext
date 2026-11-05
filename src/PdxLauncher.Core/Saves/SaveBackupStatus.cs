namespace PdxLauncher.Core.Saves;

/// <summary>备份结果状态。</summary>
public enum SaveBackupStatus
{
    /// <summary>成功。</summary>
    Ok = 0,

    /// <summary>参数不合法（路径为空、gameId 为空等）。</summary>
    InvalidArgument = 1,

    /// <summary>源存档不存在。</summary>
    SourceMissing = 2,

    /// <summary>配额超限（总量或该游戏条目数）。</summary>
    QuotaExceeded = 3,

    /// <summary>IO 失败（占用、权限、磁盘等）。</summary>
    Failed = 4,
}

/// <summary>恢复结果状态。</summary>
public enum SaveRestoreStatus
{
    /// <summary>成功。</summary>
    Ok = 0,

    /// <summary>参数不合法，或条目与 sidecar 不一致（条目已过期）。</summary>
    InvalidArgument = 1,

    /// <summary>备份正文不存在。</summary>
    BackupMissing = 2,

    /// <summary>sidecar 不存在。</summary>
    SidecarMissing = 3,

    /// <summary>备份实际大小与 sidecar 记录不一致。</summary>
    SizeMismatch = 4,

    /// <summary>SHA-256 校验不一致，未写入任何文件。</summary>
    HashMismatch = 5,

    /// <summary>IO 失败（占用、权限、磁盘等）。</summary>
    Failed = 6,
}

/// <summary>删除（备份或真实存档）结果状态。</summary>
public enum SaveDeleteStatus
{
    /// <summary>成功。</summary>
    Ok = 0,

    /// <summary>参数不合法。</summary>
    InvalidArgument = 1,

    /// <summary>目标不存在。</summary>
    NotFound = 2,

    /// <summary>IO 失败，<c>SourceStillPresent</c> 与残留清单说明现场。</summary>
    Failed = 3,
}
