namespace PdxLauncher.Checks;

using System.Security.Cryptography;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 本地翻译模型仓库的自检：安装状态五态、删除结果三态与删后复核、可选哈希校验。
/// <para>
/// 全部落在临时目录里，用一套几十字节的假清单，绝不碰真实的 %LOCALAPPDATA%\PdxLauncher\models
/// （真清单里的 Index-Translate 有 1.3 GB）。
/// </para>
/// </summary>
public static class LocalTranslationModelStoreChecks
{
    private const string IndexFileName = "index-test.gguf";
    private const string ModelFileName = "model-test.bin";
    private const string VocabFileName = "vocab-test.txt";
    private const string ZhEnFileName = "model-zhen-test.bin";

    private static readonly byte[] IndexContent = Fill(16, 0x11);
    private static readonly byte[] ModelContent = Fill(8, 0x22);
    private static readonly byte[] VocabContent = Fill(4, 0x33);
    private static readonly byte[] ZhEnContent = Fill(4, 0x44);

    /// <summary>运行自检；返回 0 表示本类断言全过。</summary>
    /// <param name="fs">文件系统抽象（本类直接用 System.IO，仅保持签名一致）。</param>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "pdxnext-model-store-checks-" + Guid.NewGuid().ToString("N"));
        FileStream? lockedFile = null;
        try
        {
            var catalog = CreateCatalog(includeZhEn: true);

            // ---------- 安装状态五态 ----------
            var stateRoot = Path.Combine(tempRoot, "state-root");
            var store = new LocalTranslationModelStore(catalog, stateRoot);
            var indexDir = LocalTranslationModelStore.GetModelDirectory(LocalTranslationModelKind.IndexTranslate, stateRoot);

            var missingDir = store.GetInstallState(LocalTranslationModelKind.IndexTranslate);
            ChecksContext.Equal(
                "模型仓库: 目录不存在 → NotInstalled",
                LocalTranslationModelInstallStatus.NotInstalled,
                missingDir.Status);
            ChecksContext.Equal("模型仓库: 未安装时目录路径仍可读", indexDir, missingDir.Directory);
            ChecksContext.Check("模型仓库: 未安装时缺文件清单为空", missingDir.MissingFiles.Count == 0);
            ChecksContext.Equal(
                "模型仓库: 未安装时 GetInstalledSize 为 0",
                0L,
                store.GetInstalledSize(LocalTranslationModelKind.IndexTranslate));

            WriteFile(indexDir, IndexFileName, IndexContent);
            var installed = store.GetInstallState(LocalTranslationModelKind.IndexTranslate);
            ChecksContext.Equal(
                "模型仓库: 文件齐全 → Installed",
                LocalTranslationModelInstallStatus.Installed,
                installed.Status);
            ChecksContext.Check("模型仓库: 已安装时 IsInstalled/IsRunnable 为真", installed.IsInstalled && installed.IsRunnable);
            ChecksContext.Check("模型仓库: 已安装时 Reason 为空", installed.Reason is null);

            File.Delete(Path.Combine(indexDir, IndexFileName));
            var missingFile = store.GetInstallState(LocalTranslationModelKind.IndexTranslate);
            ChecksContext.Equal(
                "模型仓库: 少一个文件 → MissingFiles",
                LocalTranslationModelInstallStatus.MissingFiles,
                missingFile.Status);
            ChecksContext.Check(
                "模型仓库: 缺文件时列出文件名",
                missingFile.MissingFiles.Count == 1 && missingFile.MissingFiles[0] == IndexFileName,
                string.Join(",", missingFile.MissingFiles));
            ChecksContext.Check("模型仓库: 缺文件时 IsInstalled 为假", !missingFile.IsInstalled);

            WriteFile(indexDir, IndexFileName, IndexContent[..^1]);
            var truncated = store.GetInstallState(LocalTranslationModelKind.IndexTranslate);
            ChecksContext.Equal(
                "模型仓库: 差 1 字节 → SizeMismatch",
                LocalTranslationModelInstallStatus.SizeMismatch,
                truncated.Status);
            ChecksContext.Check(
                "模型仓库: 尺寸差异写明实际 15 期望 16",
                truncated.SizeMismatches.Count == 1
                    && truncated.SizeMismatches[0].ExpectedBytes == IndexContent.Length
                    && truncated.SizeMismatches[0].ActualBytes == IndexContent.Length - 1
                    && truncated.SizeMismatches[0].DeltaBytes == -1
                    && truncated.SizeMismatches[0].Describe().Contains("15", StringComparison.Ordinal),
                truncated.SizeMismatches.Count == 0 ? "(空)" : truncated.SizeMismatches[0].Describe());
            ChecksContext.Check("模型仓库: 尺寸不符时 IsInstalled 为假", !truncated.IsInstalled);

            // 占用求和：Index 16B + Bergamot 8B + 4B，另塞一个不在清单里的文件不应被计入。
            WriteFile(indexDir, IndexFileName, IndexContent);
            var bergamotDir = LocalTranslationModelStore.GetModelDirectory(
                LocalTranslationModelKind.BergamotEnZh,
                stateRoot);
            WriteFile(bergamotDir, ModelFileName, ModelContent);
            WriteFile(bergamotDir, VocabFileName, VocabContent);
            WriteFile(bergamotDir, "leftover.tmp", Fill(1024, 0x00));
            ChecksContext.Equal(
                "模型仓库: Index 占用 16 字节",
                (long)IndexContent.Length,
                store.GetInstalledSize(LocalTranslationModelKind.IndexTranslate));
            ChecksContext.Equal(
                "模型仓库: Bergamot 占用只算清单内 12 字节",
                (long)(ModelContent.Length + VocabContent.Length),
                store.GetInstalledSize(LocalTranslationModelKind.BergamotEnZh));

            var configMissing = store.GetInstallState(LocalTranslationModelKind.BergamotEnZh);
            ChecksContext.Equal(
                "模型仓库: Bergamot 缺 config.txt → ConfigMissing",
                LocalTranslationModelInstallStatus.ConfigMissing,
                configMissing.Status);
            ChecksContext.Check(
                "模型仓库: 缺配置时 IsInstalled 仍为真（历史语义）但不可运行",
                configMissing.IsInstalled && !configMissing.IsRunnable,
                configMissing.Reason ?? "(无)");

            ChecksContext.Check(
                "模型仓库: 写 config.txt 后者变可运行",
                LocalTranslationModelConfigWriter.Write(LocalTranslationModelKind.BergamotEnZh, bergamotDir)
                    && store.GetInstallState(LocalTranslationModelKind.BergamotEnZh).IsRunnable);

            // ---------- 哈希校验（deep） ----------
            ChecksContext.Check(
                "模型仓库: deep 校验全对 → IsValid",
                store.VerifyInstalled(LocalTranslationModelKind.IndexTranslate, deep: true).IsValid);
            ChecksContext.Check(
                "模型仓库: 浅校验不算哈希",
                store.VerifyInstalled(LocalTranslationModelKind.IndexTranslate).HashMismatches.Count == 0);

            WriteFile(indexDir, IndexFileName, Fill(IndexContent.Length, 0x99));
            var corrupted = store.VerifyInstalled(LocalTranslationModelKind.IndexTranslate, deep: true);
            ChecksContext.Check(
                "模型仓库: 同尺寸换内容 → deep 报哈希不符",
                !corrupted.IsValid
                    && corrupted.HashMismatches.Count == 1
                    && corrupted.HashMismatches[0].FileName == IndexFileName
                    && !corrupted.HashMismatches[0].IsReadError,
                corrupted.HashMismatches.Count == 0 ? "(空)" : corrupted.HashMismatches[0].ActualSha256 ?? "(读失败)");
            ChecksContext.Check(
                "模型仓库: 同一份坏文件浅校验不报哈希",
                store.VerifyInstalled(LocalTranslationModelKind.IndexTranslate).HashMismatches.Count == 0);
            ChecksContext.Check(
                "模型仓库: 无 Sha256 的清单文件不参与哈希",
                store.VerifyInstalled(LocalTranslationModelKind.BergamotEnZh, deep: true).HashMismatches.Count == 0);

            // ---------- 删除 ----------
            var removeRoot = Path.Combine(tempRoot, "remove-root");
            var removeStore = new LocalTranslationModelStore(catalog, removeRoot);

            var notFound = removeStore.Remove(LocalTranslationModelKind.BergamotZhEn);
            ChecksContext.Equal(
                "模型仓库: 删除不存在的模型 → NotFound",
                LocalTranslationModelRemoveStatus.NotFound,
                notFound.Status);
            ChecksContext.Check("模型仓库: NotFound 视为干净", notFound.IsClean && notFound.RemovedFiles == 0);

            var removeIndexDir = LocalTranslationModelStore.GetModelDirectory(
                LocalTranslationModelKind.IndexTranslate,
                removeRoot);
            WriteFile(removeIndexDir, IndexFileName, IndexContent);
            var released = false;
            var removed = removeStore.Remove(
                LocalTranslationModelKind.IndexTranslate,
                releaseEngine: () => released = true);
            ChecksContext.Equal(
                "模型仓库: 删除成功 → Removed",
                LocalTranslationModelRemoveStatus.Removed,
                removed.Status);
            ChecksContext.Check(
                "模型仓库: 删除前调用了释放回调",
                released);
            ChecksContext.Check(
                "模型仓库: 删除成功后目录确实不存在",
                !Directory.Exists(removeIndexDir),
                removed.Directory);
            ChecksContext.Check(
                "模型仓库: 删除成功无残留且计 1 个文件",
                removed.IsClean && removed.Residue.Count == 0 && removed.RemovedFiles == 1,
                "残留 " + removed.Residue.Count + " 个，删掉 " + removed.RemovedFiles + " 个文件");

            // 删前释放回调抛异常也不能阻断删除：句柄可能仍在，交给删后复核如实报告。
            WriteFile(removeIndexDir, IndexFileName, IndexContent);
            var removedDespiteReleaseError = removeStore.Remove(
                LocalTranslationModelKind.IndexTranslate,
                releaseEngine: () => throw new InvalidOperationException("自检：释放回调故意失败"));
            ChecksContext.Equal(
                "模型仓库: 释放回调抛异常仍能删干净",
                LocalTranslationModelRemoveStatus.Removed,
                removedDespiteReleaseError.Status);

            // 占用文件：目录里有一个被独占打开的文件，删除必须报残留而不是成功。
            var lockedDir = LocalTranslationModelStore.GetModelDirectory(
                LocalTranslationModelKind.BergamotEnZh,
                removeRoot);
            WriteFile(lockedDir, ModelFileName, ModelContent);
            WriteFile(lockedDir, VocabFileName, VocabContent);
            lockedFile = new FileStream(
                Path.Combine(lockedDir, ModelFileName),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            var blocked = removeStore.Remove(LocalTranslationModelKind.BergamotEnZh);
            ChecksContext.Check(
                "模型仓库: 占用文件 → 报 RemovedWithResidue/Failed，绝不报 Removed",
                blocked.Status is LocalTranslationModelRemoveStatus.RemovedWithResidue
                    or LocalTranslationModelRemoveStatus.Failed,
                blocked.Status.ToString());
            ChecksContext.Check("模型仓库: 占用文件时 IsClean 为假", !blocked.IsClean);
            ChecksContext.Check(
                "模型仓库: 占用文件时给出非空残留清单",
                blocked.Residue.Count > 0,
                "[" + string.Join(", ", blocked.Residue) + "]");
            ChecksContext.Check(
                "模型仓库: 残留清单点名被占用的文件",
                blocked.Residue.Any(entry => entry == ModelFileName),
                "[" + string.Join(", ", blocked.Residue) + "]");
            ChecksContext.Check(
                "模型仓库: 占用文件时目录仍存在",
                Directory.Exists(lockedDir));
            ChecksContext.Check(
                "模型仓库: 报残留时附带错误说明",
                !string.IsNullOrEmpty(blocked.Error),
                blocked.Error ?? "(无)");

            lockedFile.Dispose();
            lockedFile = null;
            ChecksContext.Equal(
                "模型仓库: 解除占用后可删干净",
                LocalTranslationModelRemoveStatus.Removed,
                removeStore.Remove(LocalTranslationModelKind.BergamotEnZh).Status);

            // 清单里没有这个种类但目录存在：拒绝按猜测路径删，报 Failed。
            var unknownRoot = Path.Combine(tempRoot, "unknown-root");
            var unknownStore = new LocalTranslationModelStore(CreateCatalog(includeZhEn: false), unknownRoot);
            var unknownDir = LocalTranslationModelStore.GetModelDirectory(
                LocalTranslationModelKind.BergamotZhEn,
                unknownRoot);
            WriteFile(unknownDir, ZhEnFileName, ZhEnContent);
            var unknown = unknownStore.Remove(LocalTranslationModelKind.BergamotZhEn);
            ChecksContext.Equal(
                "模型仓库: 清单缺该种类 → Failed",
                LocalTranslationModelRemoveStatus.Failed,
                unknown.Status);
            ChecksContext.Check(
                "模型仓库: 拒绝删除时目录原样保留",
                Directory.Exists(unknownDir) && File.Exists(Path.Combine(unknownDir, ZhEnFileName)));
        }
        finally
        {
            lockedFile?.Dispose();
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>几十字节的假清单：尺寸与哈希都是自洽的，避免真的读 1.3 GB。</summary>
    private static LocalTranslationModelCatalog CreateCatalog(bool includeZhEn)
    {
        var index = new LocalTranslationModel(
            LocalTranslationModelKind.IndexTranslate,
            "Index-Translate（自检）",
            IndexContent.Length,
            Sha256(IndexContent),
            new[]
            {
                new LocalTranslationModelFile(
                    IndexFileName,
                    "https://example.invalid/" + IndexFileName,
                    Sha256(IndexContent),
                    IndexContent.Length),
            });

        var enzh = new LocalTranslationModel(
            LocalTranslationModelKind.BergamotEnZh,
            "Bergamot EN→ZH（自检）",
            ModelContent.Length + VocabContent.Length,
            Sha256(ModelContent),
            new[]
            {
                new LocalTranslationModelFile(
                    ModelFileName,
                    "https://example.invalid/" + ModelFileName,
                    Sha256(ModelContent),
                    ModelContent.Length),
                new LocalTranslationModelFile(
                    VocabFileName,
                    "https://example.invalid/" + VocabFileName,
                    Sha256: null,
                    VocabContent.Length),
            });

        var zhen = new LocalTranslationModel(
            LocalTranslationModelKind.BergamotZhEn,
            "Bergamot ZH→EN（自检）",
            ZhEnContent.Length,
            Sha256(ZhEnContent),
            new[]
            {
                new LocalTranslationModelFile(
                    ZhEnFileName,
                    "https://example.invalid/" + ZhEnFileName,
                    Sha256(ZhEnContent),
                    ZhEnContent.Length),
            });

        return includeZhEn
            ? new LocalTranslationModelCatalog(index, enzh, zhen)
            : new LocalTranslationModelCatalog(index, enzh);
    }

    private static void WriteFile(string directory, string fileName, byte[] content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, fileName), content);
    }

    private static byte[] Fill(int length, byte value)
    {
        var data = new byte[length];
        Array.Fill(data, value);
        return data;
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
