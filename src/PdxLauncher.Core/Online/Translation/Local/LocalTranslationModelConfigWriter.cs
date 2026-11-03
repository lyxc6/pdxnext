namespace PdxLauncher.Core.Online.Translation.Local;

using System.Text;

/// <summary>
/// 写出 Bergamot 引擎需要的 <c>config.txt</c>。
/// <para>
/// 模板与 Mozilla 官方模型一致；<c>relative-paths: true</c> 表示模型文件都相对
/// 配置文件所在目录查找。中文方向必须列两份词表，否则原生库会直接 abort。
/// </para>
/// </summary>
public static class LocalTranslationModelConfigWriter
{
    /// <summary>Bergamot 配置文件名。</summary>
    public const string ConfigFileName = "config.txt";

    private const string CommonOptions = """
        beam-size: 1
        normalize: 1.0
        word-penalty: 0
        max-length-break: 128
        mini-batch-words: 1024
        workspace: 128
        max-length-factor: 2.0
        skip-cost: true
        cpu-threads: 0
        quiet: true
        quiet-translation: true
        gemm-precision: int8shiftAlphaAll
        """;

    /// <summary>为指定模型写出配置文件；Index-Translate 不需要，直接返回 false。</summary>
    /// <param name="kind">模型种类。</param>
    /// <param name="directory">模型目录。</param>
    public static bool Write(LocalTranslationModelKind kind, string directory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("relative-paths: true");
        builder.AppendLine("models:");

        switch (kind)
        {
            case LocalTranslationModelKind.BergamotEnZh:
                builder.AppendLine("- model.enzh.intgemm.alphas.bin");
                builder.AppendLine("vocabs:");
                builder.AppendLine("- srcvocab.enzh.spm");
                builder.AppendLine("- trgvocab.enzh.spm");
                builder.AppendLine("shortlist:");
                builder.AppendLine("- lex.50.50.enzh.s2t.bin");
                break;

            case LocalTranslationModelKind.BergamotZhEn:
                builder.AppendLine("- model.zhen.intgemm.alphas.bin");
                builder.AppendLine("vocabs:");
                builder.AppendLine("- vocab.zhen.src.spm");
                builder.AppendLine("- vocab.zhen.trg.spm");
                builder.AppendLine("shortlist:");
                builder.AppendLine("- lex.50.50.zhen.s2t.bin");
                break;

            default:
                return false;
        }

        builder.AppendLine("- false");
        builder.AppendLine(CommonOptions);

        var path = Path.Combine(directory, ConfigFileName);
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        return true;
    }
}
