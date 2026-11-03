namespace PdxLauncher.Core.Online.Translation.Local;

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

    private readonly LocalTranslationModelCatalog _catalog;

    /// <summary>创建模型仓库。</summary>
    /// <param name="catalog">模型清单。</param>
    public LocalTranslationModelStore(LocalTranslationModelCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>模型根目录（纯 ASCII 路径）。</summary>
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        RootFolderName,
        ModelsFolderName);

    /// <summary>某个模型所在的目录。</summary>
    public static string GetModelDirectory(LocalTranslationModelKind kind) =>
        Path.Combine(RootDirectory, kind switch
        {
            LocalTranslationModelKind.IndexTranslate => "index-translate-2b",
            LocalTranslationModelKind.BergamotEnZh => "bergamot-en-zh",
            LocalTranslationModelKind.BergamotZhEn => "bergamot-zh-en",
            _ => "unknown",
        });

    /// <summary>判断模型是否已经装好（文件齐全且大小对得上）。</summary>
    public bool IsInstalled(LocalTranslationModelKind kind)
    {
        var model = _catalog.Get(kind);
        if (model is null)
        {
            return false;
        }

        var directory = GetModelDirectory(kind);
        foreach (var file in model.Files)
        {
            var path = Path.Combine(directory, file.FileName);
            if (!File.Exists(path))
            {
                return false;
            }

            if (file.ExpectedSize > 0 && new FileInfo(path).Length != file.ExpectedSize)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>模型当前占用的磁盘字节数；没装返回 0。</summary>
    public long GetInstalledSize(LocalTranslationModelKind kind)
    {
        var model = _catalog.Get(kind);
        if (model is null)
        {
            return 0;
        }

        var directory = GetModelDirectory(kind);
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

    /// <summary>删除已安装的模型；未安装时什么都不做。</summary>
    public void Remove(LocalTranslationModelKind kind)
    {
        var directory = GetModelDirectory(kind);
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // 文件被占用（例如模型正被引擎加载）：留给用户下次再试。
        }
        catch (UnauthorizedAccessException)
        {
            // 权限不足不是致命错误。
        }
    }
}
