namespace PdxLauncher.Core.Signatures;

using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// <see cref="IDlcSignatureService"/> 实现：复刻官方启动器的 dlc_signature 算法。
/// <para>算法（官方 fMe）：md5( 255 字节缓冲写入 normalizedMachineId + 按 DLC name 排序的原始字节 + salt )，
/// salt = "DontStealMyGamePlz__WINNERS_DONT_USE_DRUGS__DONT_COPY_THAT_FLOPPY\0"。</para>
/// <para>normalizedMachineId（官方 Windows 分支 <c>bit()</c>）：MachineGuid 先去掉所有空白并转小写得到 n，
/// 结果为 <c>(n + "\0") + 交错串(n 每个字符后插 '\0')的后半截</c>，总长 2n。
/// 例如 "abc" → "abc\0c\0"；真机 GUID（36 字符）→ 72 字符，首字符是原 GUID 首字符，第 37 个字符是 '\0'。</para>
/// <para>默认只在设置页手动触发写入；启动流程不写 dlc_signature（官方校验逻辑与磁盘值本身不一致）。</para>
/// </summary>
public sealed class DlcSignatureService : IDlcSignatureService
{
    /// <summary>签名文件名（位于 GameDataDir 下，无扩展名）。</summary>
    public const string FileName = "dlc_signature";

    /// <summary>官方 salt 明文（尾部 NUL 单独追加）。</summary>
    public const string SignatureSalt = "DontStealMyGamePlz__WINNERS_DONT_USE_DRUGS__DONT_COPY_THAT_FLOPPY";

    /// <summary>写入 machineId 的缓冲区大小（官方 Buffer.alloc(255)）。</summary>
    public const int MachineIdBufferSize = 255;

    private const string MachineGuidKeyPath = @"SOFTWARE\Microsoft\Cryptography";
    private const string MachineGuidValueName = "MachineGuid";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly byte[] SaltBytes = BuildSaltBytes();

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>创建 DLC 签名服务。</summary>
    public DlcSignatureService(IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>dlc_signature 的完整路径。</summary>
    public string GetSignaturePath(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return _fileSystem.Combine(game.GameDataDir, FileName);
    }

    /// <inheritdoc />
    public Task<string> ComputeAsync(
        GameInstallation game,
        IReadOnlyList<DlcInfo> dlcs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var machineGuid = TryReadMachineGuid();
        if (machineGuid is null)
        {
            _logger.Warn("读取 MachineGuid 失败，dlc_signature 将基于空 machineId 计算");
        }

        var signature = ComputeSignature(machineGuid ?? string.Empty, dlcs);
        _logger.Info("已计算 dlc_signature：" + signature + "（DLC " + (dlcs?.Count ?? 0) + " 个）");
        return Task.FromResult(signature);
    }

    /// <inheritdoc />
    public Task WriteAsync(
        GameInstallation game,
        IReadOnlyList<DlcInfo> dlcs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var signature = ComputeSignature(TryReadMachineGuid() ?? string.Empty, dlcs);
        var path = GetSignaturePath(game);
        _fileSystem.EnsureDirectory(game.GameDataDir);

        // UTF-8、无 BOM、无换行：官方写的就是 32 个 ASCII 字符。
        _fileSystem.WriteAllBytesAtomic(path, Utf8NoBom.GetBytes(signature));
        _logger.Info("已写入 dlc_signature：" + path + "（" + signature + "）");
        return Task.CompletedTask;
    }

    /// <summary>纯函数版本：给定 machineId 与 DLC 列表计算签名，便于自检做确定性断言。</summary>
    public static string ComputeSignature(string? machineGuid, IReadOnlyList<DlcInfo>? dlcs)
    {
        var normalized = NormalizeMachineId(machineGuid);
        var normalizedBytes = Utf8NoBom.GetBytes(normalized);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        // 官方 Buffer.alloc(255) 后 write(...)：超长部分被截断，短了补 0。
        var buffer = new byte[MachineIdBufferSize];
        var copyLength = Math.Min(normalizedBytes.Length, buffer.Length);
        if (copyLength > 0)
        {
            Array.Copy(normalizedBytes, buffer, copyLength);
        }

        hash.AppendData(buffer);

        // lodash sortBy 稳定升序，比较的是 UTF-16 code unit（对应 StringComparer.Ordinal）。
        if (dlcs is not null)
        {
            foreach (var dlc in dlcs.OrderBy(d => d.DisplayName ?? string.Empty, StringComparer.Ordinal))
            {
                if (dlc.RawBytes is { Length: > 0 })
                {
                    hash.AppendData(dlc.RawBytes);
                }
            }
        }

        hash.AppendData(SaltBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// machineId 归一化（官方 <c>bit()</c>）：先按 node-machine-id 的 win32 分支去掉所有空白并转小写得到 n，
    /// 再计算 <c>(n + "\0") + 交错串后半截</c>，结果长度恒为 <c>2 * n.Length</c>。
    /// <para>官方源码：<c>bit = n =&gt; { const e = `${n}\0`; const t = n.split("").map(r =&gt; `${r}\0`).join(""); return [e, t.substring(e.length, t.length)].join(""); }</c></para>
    /// <para>例："abc" → "abc\0c\0"；空输入 → 空串。</para>
    /// </summary>
    public static string NormalizeMachineId(string? machineGuid)
    {
        if (string.IsNullOrEmpty(machineGuid))
        {
            return string.Empty;
        }

        // 第一步：去掉所有空白字符并转小写（等价 replace(/\r+|\n+|\s+/gi, "").toLowerCase()）。
        var builder = new StringBuilder(machineGuid.Length);
        foreach (var ch in machineGuid)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        var normalized = builder.ToString();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        // 第二步：head = n + "\0"，tail = 每个字符后插 '\0' 的交错串。
        var head = normalized + "\0";
        var interleaved = new StringBuilder(normalized.Length * 2);
        foreach (var ch in normalized)
        {
            interleaved.Append(ch);
            interleaved.Append('\0');
        }

        var tail = interleaved.ToString();

        // 第三步：head 接上 tail 从 head.Length 开始的后半截（截掉与 head 重复的前缀）。
        return head + tail.Substring(head.Length, tail.Length - head.Length);
    }

    /// <summary>读取本机 MachineGuid（HKLM\SOFTWARE\Microsoft\Cryptography）；失败返回 null。</summary>
    public static string? TryReadMachineGuid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MachineGuidKeyPath, writable: false);
            if (key?.GetValue(MachineGuidValueName) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static byte[] BuildSaltBytes()
    {
        var salt = Utf8NoBom.GetBytes(SignatureSalt);
        var bytes = new byte[salt.Length + 1];
        Array.Copy(salt, bytes, salt.Length);
        bytes[salt.Length] = 0;
        return bytes;
    }
}
