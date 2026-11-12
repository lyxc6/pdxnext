namespace PdxLauncher.Core.Online.Translation.Local;

using System.Security.Cryptography;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 本地翻译模型的下载与安装服务。
/// <para>
/// 负责三件事：算出模型该放哪、判断模型是否已经装好、按需下载并解压。
/// </para>
/// <para>
/// 目录不能放在数据根目录下：Bergamot 与 llama.cpp 的原生层都按 ANSI 处理路径，
/// 路径里出现中文会直接加载失败。所以模型一律落到 <c>%LOCALAPPDATA%\PdxLauncher\models\</c>，
/// 这一层是纯 ASCII 路径。
/// </para>
/// </summary>
public sealed class LocalTranslationModelStore
{
    /// <summary>模型根目录名（放在 %LOCALAPPDATA% 下）。</summary>
    public const string RootFolderName = "PdxLauncher";

    /// <summary>模型目录名。</summary>
    public const string ModelsFolderName = "models";

    /// <summary>Index-Translate 官方仓库直链前缀。</summary>
    public const string IndexTranslateBaseUrl =
        "https://huggingface.co/IndexTeam/Index-Translate-2B-GGUF/resolve/main/";

    /// <summary>Mozilla 翻译数据仓库根地址。</summary>
    public const string MozillaBaseUrl =
        "https://storage.googleapis.com/moz-fx-translations-data--303e-prod-translations-data/";

    /// <summary>Index-Translate 模型文件名。</summary>
    public const string IndexTranslateFileName = "Index-Translate-2B.Q4_K_M.gguf";

    /// <summary>Index-Translate 模型的 SHA-256（与官方 SHA256SUMS.txt 一致）。</summary>
    public const string IndexTranslateSha256 =
        "044b313d29342bd3b2c77cbb64023ca0d209bd9b3247763f9d162767ef2d746a";

    /// <summary>Index-Translate 模型字节数。</summary>
    public const long IndexTranslateSize = 1312164352;

    /// <summary>首次删除失败后，等这么久再重试一次（毫秒）。</summary>
    private const int RetryDelayMilliseconds = 150;

    private readonly LocalTranslationModelCatalog _catalog;
    private readonly string _rootDirectory;
    private readonly IAppLogger? _logger;

    /// <summary>创建模型仓库（使用默认根目录、不记日志）。</summary>
    /// <param name="catalog">模型清单。</param>
    public LocalTranslationModelStore(LocalTranslationModelCatalog catalog)
        : this(catalog, rootDirectory: null)
    {
    }

    /// <summary>创建模型仓库。</summary>
    /// <param name="catalog">模型清单。</param>
    /// <param name="rootDirectory">模型根目录；null/空表示用默认的 <see cref="RootDirectory"/>（自检用临时目录）。</param>
    /// <param name="logger">日志；删除失败与残留会写进来。</param>
    public LocalTranslationModelStore(
        LocalTranslationModelCatalog catalog,
        string? rootDirectory,
        IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? RootDirectory : rootDirectory;
        _logger = logger;
    }

    /// <summary>模型根目录（纯 ASCII 路径）。</summary>
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        RootFolderName,
        ModelsFolderName);

    /// <summary>某个模型所在的目录。</summary>
    /// <param name="kind">模型种类。</param>
    /// <param name="rootDirectory">根目录覆盖（自检用）；null 表示默认根目录。</param>
    /// <returns>模型目录的绝对路径（不保证存在）。</returns>
    public static string GetModelDirectory(LocalTranslationModelKind kind, string? rootDirectory = null) =>
        Path.Combine(
            string.IsNullOrWhiteSpace(rootDirectory) ? RootDirectory : rootDirectory,
            kind switch
            {
                LocalTranslationModelKind.IndexTranslate => "index-translate-2b",
                LocalTranslationModelKind.BergamotEnZh => "bergamot-en-zh",
                LocalTranslationModelKind.BergamotZhEn => "bergamot-zh-en",
                _ => "unknown",
            });

    /// <summary>
    /// 取得模型此刻的安装状态（未安装 / 缺文件 / 尺寸不符 / 配置缺失 / 已安装）。
    /// <para>
    /// 只读文件系统，不算哈希，因此可以在界面刷新时反复调用。
    /// </para>
    /// </summary>
    /// <param name="kind">模型种类。</param>
    /// <returns>状态快照；清单里没有该模型时返回 NotInstalled。</returns>
    public LocalTranslationModelState GetInstallState(LocalTranslationModelKind kind)
    {
        var directory = DirectoryOf(kind);
        var model = _catalog.Get(kind);
        if (model is null)
        {
            return new LocalTranslationModelState(
                kind,
                LocalTranslationModelInstallStatus.NotInstalled,
                directory,
                Array.Empty<string>(),
                Array.Empty<LocalTranslationModelSizeMismatch>(),
                "模型清单里没有这个种类。");
        }

        if (!Directory.Exists(directory))
        {
            return new LocalTranslationModelState(
                kind,
                LocalTranslationModelInstallStatus.NotInstalled,
                directory,
                Array.Empty<string>(),
                Array.Empty<LocalTranslationModelSizeMismatch>(),
                "模型目录不存在。");
        }

        var missing = new List<string>();
        var mismatches = new List<LocalTranslationModelSizeMismatch>();
        foreach (var file in model.Files)
        {
            var path = Path.Combine(directory, file.FileName);
            if (!File.Exists(path))
            {
                missing.Add(file.FileName);
                continue;
            }

            if (file.ExpectedSize <= 0)
            {
                continue;
            }

            var actual = new FileInfo(path).Length;
            if (actual != file.ExpectedSize)
            {
                mismatches.Add(new LocalTranslationModelSizeMismatch(file.FileName, file.ExpectedSize, actual));
            }
        }

        LocalTranslationModelInstallStatus status;
        string? reason;
        if (missing.Count > 0)
        {
            status = LocalTranslationModelInstallStatus.MissingFiles;
            reason = "缺少文件：" + string.Join("、", missing);
        }
        else if (mismatches.Count > 0)
        {
            status = LocalTranslationModelInstallStatus.SizeMismatch;
            reason = "文件大小不符：" + string.Join("；", mismatches.Select(mismatch => mismatch.Describe()));
        }
        else if (RequiresConfig(kind)
            && !File.Exists(Path.Combine(directory, LocalTranslationModelConfigWriter.ConfigFileName)))
        {
            status = LocalTranslationModelInstallStatus.ConfigMissing;
            reason = "缺少 " + LocalTranslationModelConfigWriter.ConfigFileName + "，引擎无法加载该模型。";
        }
        else
        {
            status = LocalTranslationModelInstallStatus.Installed;
            reason = null;
        }

        return new LocalTranslationModelState(kind, status, directory, missing, mismatches, reason);
    }

    /// <summary>
    /// 判断模型是否已经装好（文件齐全且大小对得上）。
    /// <para>
    /// 「装没装好」的唯一真源：界面/引擎都走这里，不要各自再算一套。
    /// 缺 <c>config.txt</c> 不影响本判定（历史语义），完整原因见 <see cref="GetInstallState"/>；
    /// 哈希也不在这里算（1.3 GB 太贵），见 <see cref="VerifyInstalled"/>。
    /// </para>
    /// </summary>
    /// <param name="kind">模型种类。</param>
    /// <returns>文件齐全且尺寸正确即为 true。</returns>
    public bool IsInstalled(LocalTranslationModelKind kind) => GetInstallState(kind).IsInstalled;

    /// <summary>模型当前占用的磁盘字节数；没装返回 0。</summary>
    /// <param name="kind">模型种类。</param>
    /// <returns>清单内文件的字节数之和（不统计目录里的其它残留文件）。</returns>
    public long GetInstalledSize(LocalTranslationModelKind kind)
    {
        var model = _catalog.Get(kind);
        if (model is null)
        {
            return 0;
        }

        var directory = DirectoryOf(kind);
        long total = 0;
        foreach (var file in model.Files)
        {
            var path = Path.Combine(directory, file.FileName);
            if (File.Exists(path))
            {
                total += new FileInfo(path).Length;
            }
        }

        return total;
    }

    /// <summary>
    /// 校验模型文件。
    /// <para>
    /// <paramref name="deep"/> 为 false 时只做存在性与尺寸检查；为 true 时再对清单里带
    /// SHA-256 的文件（Index 的 gguf、Bergamot 两个主 bin）逐个算哈希。
    /// </para>
    /// <para>
    /// 哈希校验<strong>故意</strong>不放进 <see cref="IsInstalled"/>：Index-Translate 的 gguf 有 1.3 GB，
    /// 每次刷新界面都读一遍太贵。这里留给以后 UI 的「校验」按钮按需调用。
    /// </para>
    /// </summary>
    /// <param name="kind">模型种类。</param>
    /// <param name="deep">是否做 SHA-256 校验。</param>
    /// <returns>基础状态与哈希差异。</returns>
    public LocalTranslationModelVerifyResult VerifyInstalled(LocalTranslationModelKind kind, bool deep = false)
    {
        var state = GetInstallState(kind);
        if (!deep)
        {
            return new LocalTranslationModelVerifyResult(state, Array.Empty<LocalTranslationModelHashMismatch>());
        }

        var mismatches = new List<LocalTranslationModelHashMismatch>();
        var model = _catalog.Get(kind);
        if (model is not null)
        {
            foreach (var file in model.Files)
            {
                if (string.IsNullOrEmpty(file.Sha256))
                {
                    continue;
                }

                var path = Path.Combine(state.Directory, file.FileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 128 * 1024,
                        FileOptions.SequentialScan);
                    var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
                    if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches.Add(new LocalTranslationModelHashMismatch(file.FileName, file.Sha256, actual, null));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    mismatches.Add(new LocalTranslationModelHashMismatch(file.FileName, file.Sha256, null, ex.Message));
                }
            }
        }

        return new LocalTranslationModelVerifyResult(state, mismatches);
    }

    /// <summary>
    /// 删除已安装的模型。
    /// <para>
    /// 先调用 <paramref name="releaseEngine"/> 释放引擎持有的文件句柄，再逐个文件删除，
    /// 失败会重试一次；最后一定复核目录是否真的消失——只有复核通过才报 Removed。
    /// <c>IOException</c> / <c>UnauthorizedAccessException</c> 不再被吞掉：写日志并写进结果。
    /// </para>
    /// </summary>
    /// <param name="kind">模型种类。</param>
    /// <param name="releaseEngine">删除前释放引擎句柄的回调；调用方必须保证没有在途翻译。</param>
    /// <returns>删除结果（Removed / RemovedWithResidue / NotFound / Failed）。</returns>
    public LocalTranslationModelRemoveResult Remove(LocalTranslationModelKind kind, Action? releaseEngine = null)
    {
        var directory = DirectoryOf(kind);
        if (!Directory.Exists(directory))
        {
            return new LocalTranslationModelRemoveResult(
                kind,
                LocalTranslationModelRemoveStatus.NotFound,
                directory,
                0,
                Array.Empty<string>(),
                null);
        }

        if (_catalog.Get(kind) is null)
        {
            const string reason = "模型清单里没有这个种类，拒绝按猜测的路径删除。";
            _logger?.Warn($"删除本地翻译模型被拒绝：{kind}（{reason}）");
            return new LocalTranslationModelRemoveResult(
                kind,
                LocalTranslationModelRemoveStatus.Failed,
                directory,
                0,
                Array.Empty<string>(),
                reason);
        }

        var errors = new List<string>();
        if (releaseEngine is not null)
        {
            try
            {
                releaseEngine();
            }
            catch (Exception ex)
            {
                // 释放失败不阻断删除：句柄可能仍被占用，下面的复核会把残留如实报出来。
                errors.Add("释放引擎句柄失败：" + ex.Message);
                _logger?.Error($"释放本地翻译引擎句柄失败：{kind}", ex);
            }
        }

        try
        {
            var before = CountFiles(directory);

            // 逐个删文件而不是直接递归删目录：这样删到一半被占用时，能精确知道哪些还在。
            DeleteFiles(directory, errors);
            if (!TryDeleteDirectory(directory, errors))
            {
                // Windows 释放句柄通常只要几毫秒；给一次短重试，避免「手快」的假失败。
                Thread.Sleep(RetryDelayMilliseconds);
                DeleteFiles(directory, errors);
                TryDeleteDirectory(directory, errors);
            }

            if (!Directory.Exists(directory))
            {
                _logger?.Info($"本地翻译模型已删除：{kind}（{before} 个文件，目录 {directory}）");

                // 目录已经不在，Result 就是干净的：释放回调本身的异常只记日志，
                // 不让调用方把「删成功了」误读成失败。
                return new LocalTranslationModelRemoveResult(
                    kind,
                    LocalTranslationModelRemoveStatus.Removed,
                    directory,
                    before,
                    Array.Empty<string>(),
                    null);
            }

            var residue = CollectResidue(directory);
            var remainingFiles = residue.Count(entry => entry != "." && !entry.EndsWith('/'));
            var message = JoinErrors(errors) ?? "模型目录仍然存在。";
            _logger?.Warn($"本地翻译模型删除后仍有残留：{kind}（{string.Join(", ", residue)}）{message}");
            return new LocalTranslationModelRemoveResult(
                kind,
                LocalTranslationModelRemoveStatus.RemovedWithResidue,
                directory,
                Math.Max(0, before - remainingFiles),
                residue,
                message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger?.Error($"删除本地翻译模型失败：{kind}", ex);
            return new LocalTranslationModelRemoveResult(
                kind,
                LocalTranslationModelRemoveStatus.Failed,
                directory,
                0,
                Array.Empty<string>(),
                ex.Message);
        }
    }

    /// <summary>Bergamot 走原生库，必须有 config.txt；Index-Translate 不需要。</summary>
    private static bool RequiresConfig(LocalTranslationModelKind kind) =>
        kind is LocalTranslationModelKind.BergamotEnZh or LocalTranslationModelKind.BergamotZhEn;

    private string DirectoryOf(LocalTranslationModelKind kind) => GetModelDirectory(kind, _rootDirectory);

    private static int CountFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count();

    private static void DeleteFiles(string directory, List<string> errors)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                errors.Add(RelativePath(directory, path) + "：" + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(RelativePath(directory, path) + "：" + ex.Message);
            }
        }
    }

    /// <summary>尝试递归删目录；返回目录是否已经不存在。</summary>
    private static bool TryDeleteDirectory(string directory, List<string> errors)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            errors.Add(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add(ex.Message);
        }

        return !Directory.Exists(directory);
    }

    /// <summary>
    /// 列出仍存在的条目（相对模型目录、用 / 分隔）。空目录以 / 结尾；一个文件都没有时用
    /// <c>.</c> 表示目录本身还在。枚举失败会向上抛，由调用方归入 Failed。
    /// </summary>
    private static IReadOnlyList<string> CollectResidue(string directory)
    {
        var residue = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            residue.Add(RelativePath(directory, path));
        }

        foreach (var path in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            residue.Add(RelativePath(directory, path) + "/");
        }

        if (residue.Count == 0)
        {
            residue.Add(".");
        }

        return residue;
    }

    private static string RelativePath(string directory, string path) =>
        Path.GetRelativePath(directory, path).Replace('\\', '/');

    private static string? JoinErrors(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? null : string.Join("；", errors.Distinct(StringComparer.Ordinal));
}
