namespace PdxLauncher.Core.Updates;

/// <summary>
/// 安装包已下载并校验，但改名为正式文件时被其它进程占用（杀毒扫描、索引器、资源管理器等）。
/// 此时 <c>.part</c> 会原样保留，下次下载可直接复用；界面应提示用户稍后重试。
/// </summary>
public sealed class UpdatePackageLockedException : IOException
{
    public UpdatePackageLockedException(string tempPath, string finalPath, Exception innerException)
        : base("更新安装包已下载并校验，但改名时被占用: " + finalPath, innerException)
    {
        TempPath = tempPath;
        FinalPath = finalPath;
    }

    /// <summary>保留的完整临时文件（<c>*.part</c>）。</summary>
    public string TempPath { get; }

    /// <summary>目标安装包路径。</summary>
    public string FinalPath { get; }
}
