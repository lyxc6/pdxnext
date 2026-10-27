namespace PdxLauncher.Core.Launching;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// <see cref="IGameLauncher"/> 实现：把 <see cref="GameInstallation"/> + <see cref="LaunchRequest"/>
/// 翻译成绝对可执行文件路径、工作目录与参数列表，再交给 <see cref="IProcessLauncher"/> 分离启动。
/// <para>参数顺序固定为：请求里的自定义参数 → <c>--continuelastsave</c>（仅继续游戏时）→ launcher-settings 的 exeArgs。</para>
/// </summary>
public sealed class GameLauncher : IGameLauncher
{
    /// <summary>继续上一局存档时追加的官方参数。</summary>
    public const string ContinueLastSaveArgument = "--continuelastsave";

    private readonly IProcessLauncher _processLauncher;
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>创建游戏启动器。</summary>
    public GameLauncher(IProcessLauncher processLauncher, IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _processLauncher = processLauncher;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProcessStartResult> LaunchAsync(
        GameInstallation game,
        LaunchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var exePath = ResolveExecutablePath(game, request);
        var workingDirectory = _fileSystem.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(workingDirectory))
        {
            workingDirectory = game.InstallRoot;
        }

        if (!_fileSystem.FileExists(exePath))
        {
            _logger.Warn("启动失败：找不到可执行文件 " + exePath);
            return ProcessStartResult.Failure("找不到可执行文件: " + exePath);
        }

        var args = BuildArguments(game, request);
        _logger.Info(
            "启动游戏 " + game.GameId + "：" + exePath
            + "（工作目录 " + workingDirectory + "，参数 " + args.Count + " 个）");

        return await _processLauncher
            .StartDetachedAsync(exePath, args, workingDirectory, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 绝对化可执行文件路径：相对路径以 <see cref="GameInstallation.InstallRoot"/> 为基准，
    /// 已是绝对路径时 <c>Path.Combine</c> 会直接返回该路径，因此两种输入都能正确处理。
    /// </summary>
    public string ResolveExecutablePath(GameInstallation game, LaunchRequest? request)
    {
        ArgumentNullException.ThrowIfNull(game);
        var rawPath = string.IsNullOrWhiteSpace(request?.CustomExePath)
            ? game.ExePath
            : request!.CustomExePath!;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        return _fileSystem.GetFullPath(_fileSystem.Combine(game.InstallRoot, rawPath));
    }

    /// <summary>组装启动参数：自定义参数 → --continuelastsave → launcher-settings 的 exeArgs。</summary>
    public IReadOnlyList<string> BuildArguments(GameInstallation game, LaunchRequest? request)
    {
        ArgumentNullException.ThrowIfNull(game);
        var args = new List<string>();

        if (request?.CustomExeArgs is not null)
        {
            foreach (var arg in request.CustomExeArgs)
            {
                if (!string.IsNullOrEmpty(arg))
                {
                    args.Add(arg);
                }
            }
        }

        if (request?.ContinueLastSave == true)
        {
            args.Add(ContinueLastSaveArgument);
        }

        if (game.ExeArgs is not null)
        {
            foreach (var arg in game.ExeArgs)
            {
                if (!string.IsNullOrEmpty(arg))
                {
                    args.Add(arg);
                }
            }
        }

        return args;
    }
}
