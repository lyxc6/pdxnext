namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 可下载的本地翻译模型清单。
/// <para>
/// 数据来源：
/// <list type="bullet">
/// <item>Index-Translate-2B：Hugging Face 上的 <c>IndexTeam/Index-Translate-2B-GGUF</c>（Apache-2.0）。</item>
/// <item>Bergamot 英译中/中译英：Mozilla 翻译数据仓库的 <c>en-zh</c> 与 <c>zh-en</c> 方向模型。</item>
/// </list>
/// </para>
/// </summary>
public sealed class LocalTranslationModelCatalog
{
    private readonly IReadOnlyList<LocalTranslationModel> _models;

    /// <summary>创建模型清单。</summary>
    /// <param name="models">模型列表。</param>
    public LocalTranslationModelCatalog(params LocalTranslationModel[] models)
    {
        _models = models;
    }

    /// <summary>清单里的全部模型。</summary>
    public IReadOnlyList<LocalTranslationModel> Models => _models;

    /// <summary>按种类取模型；未知种类返回 null。</summary>
    public LocalTranslationModel? Get(LocalTranslationModelKind kind) =>
        _models.FirstOrDefault(model => model.Kind == kind);

    /// <summary>默认清单：Index-Translate-2B 加 Bergamot 两个方向。</summary>
    public static LocalTranslationModelCatalog CreateDefault() =>
        new(CreateIndexTranslate(), CreateBergamotEnZh(), CreateBergamotZhEn());

    private static LocalTranslationModel CreateIndexTranslate()
    {
        var file = new LocalTranslationModelFile(
            LocalTranslationModelStore.IndexTranslateFileName,
            LocalTranslationModelStore.IndexTranslateBaseUrl + LocalTranslationModelStore.IndexTranslateFileName,
            LocalTranslationModelStore.IndexTranslateSha256,
            LocalTranslationModelStore.IndexTranslateSize);

        return new LocalTranslationModel(
            LocalTranslationModelKind.IndexTranslate,
            "Index-Translate-2B (Q4_K_M)",
            LocalTranslationModelStore.IndexTranslateSize,
            LocalTranslationModelStore.IndexTranslateSha256,
            new[] { file });
    }

    private static LocalTranslationModel CreateBergamotEnZh()
    {
        const string prefix =
            "models/en-zh/llmaat_finetune10M_qe8_f2_ByQcSxGXQRqGi-UTxYE43g/exported/";

        var files = new[]
        {
            new LocalTranslationModelFile(
                "model.enzh.intgemm.alphas.bin",
                LocalTranslationModelStore.MozillaBaseUrl + prefix + "model.enzh.intgemm.alphas.bin.gz",
                "4e5accc141373565ddc8fa1565bceaa8d0c3482a82cab8131c719ebcc6c2157c",
                43849787,
                Compressed: true),
            new LocalTranslationModelFile(
                "lex.50.50.enzh.s2t.bin",
                LocalTranslationModelStore.MozillaBaseUrl + prefix + "lex.50.50.enzh.s2t.bin.gz",
                ExpectedSize: 4485184,
                Compressed: true),
            new LocalTranslationModelFile(
                "srcvocab.enzh.spm",
                LocalTranslationModelStore.MozillaBaseUrl + prefix + "srcvocab.enzh.spm.gz",
                ExpectedSize: 806952,
                Compressed: true),
            new LocalTranslationModelFile(
                "trgvocab.enzh.spm",
                LocalTranslationModelStore.MozillaBaseUrl + prefix + "trgvocab.enzh.spm.gz",
                ExpectedSize: 772004,
                Compressed: true),
        };

        return new LocalTranslationModel(
            LocalTranslationModelKind.BergamotEnZh,
            "Bergamot 英译中",
            43849787L + 4485184L + 806952L + 772004L,
            "4e5accc141373565ddc8fa1565bceaa8d0c3482a82cab8131c719ebcc6c2157c",
            files);
    }

    private static LocalTranslationModel CreateBergamotZhEn()
    {
        const string prefix =
            "models/zh-en/cjk_icu_base_LQeOIbF7Sbq3XA8lsRPotw/exported/";

        // Mozilla 的 zh-en 方向只提供一份词表，原生库要求源/目标两份，
        // 所以这里列出两个文件名指向同一个下载地址。
        var files = new[]
        {
            new LocalTranslationModelFile(
                "model.zhen.intgemm.alphas.bin",
                LocalTranslationModelStore.MozillaBaseUrl + prefix + "model.zhen.intgemm.alphas.bin.gz",
                "3535442962ec8f4a553cc19b206befcac689ee9cddaea44fa91e21527fc30ac2",
                59504955,
                Compressed: true),
            new LocalTranslationModelFile(
                "lex.50.50.zhen.s2t.bin",
                LocalTranslationModelStore.MozillaBaseUrl + prefix + "lex.50.50.zhen.s2t.bin.gz",
                ExpectedSize: 9220016,
                Compressed: true),
            new LocalTranslationModelFile(
                "vocab.zhen.src.spm",
                LocalTranslationModelStore.MozillaBaseUrl + prefix + "vocab.zhen.spm.gz",
                ExpectedSize: 1359697,
                Compressed: true),
            new LocalTranslationModelFile(
                "vocab.zhen.trg.spm",
                LocalTranslationModelStore.MozillaBaseUrl + prefix + "vocab.zhen.spm.gz",
                ExpectedSize: 1359697,
                Compressed: true),
        };

        return new LocalTranslationModel(
            LocalTranslationModelKind.BergamotZhEn,
            "Bergamot 中译英",
            59504955L + 9220016L + 1359697L * 2,
            "3535442962ec8f4a553cc19b206befcac689ee9cddaea44fa91e21527fc30ac2",
            files);
    }
}
