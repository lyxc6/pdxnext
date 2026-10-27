namespace PdxLauncher.App.Services;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 用 Windows DPAPI（<c>CryptProtectData</c> / <c>CryptUnprotectData</c>）保护敏感字符串。
/// <para>
/// 密文与<b>当前 Windows 用户</b>绑定：换用户、换机器、重装系统后都解不开。
/// 这正好是「会话文件被拷走也没用」的语义，解不开时按未登录处理即可。
/// </para>
/// <para>
/// DPAPI 不可用（非 Windows、系统调用失败）时自动降级为 Base64 明文方案，
/// 此时 <see cref="Scheme"/> 返回 <c>plain-base64</c>，日志里会有 Warn，但功能不中断。
/// </para>
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <summary>DPAPI 前缀：区分「这是 DPAPI 密文」与「这是降级后的 Base64」。</summary>
    private const string DpapiPrefix = "dpapi:";

    private const string PlainPrefix = "plain:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PdxNext.PdxSession.v1");

    private readonly IAppLogger _logger;
    private readonly bool _available;

    /// <summary>创建保护器；构造时探测一次 DPAPI 是否可用。</summary>
    public DpapiSecretProtector(IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _available = Probe();
        if (!_available)
        {
            _logger.Warn("DPAPI 不可用，Paradox 会话将以 Base64 形式落盘（不安全，仅用于非 Windows 环境）。");
        }
    }

    /// <inheritdoc />
    public string Scheme => _available ? "dpapi" : "plain-base64";

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (_available)
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            try
            {
                return DpapiPrefix + Convert.ToBase64String(Encrypt(bytes));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException or OutOfMemoryException)
            {
                _logger.Warn("DPAPI 加密失败，本条降级为 Base64: " + ex.Message);
            }
        }

        return PlainPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
    }

    /// <inheritdoc />
    public string? Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return null;
        }

        if (protectedText.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            try
            {
                var cipher = Convert.FromBase64String(protectedText[DpapiPrefix.Length..]);
                return Encoding.UTF8.GetString(Decrypt(cipher));
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ExternalException or CryptographicException)
            {
                // 换了用户/机器、或文件被改过：按未登录处理，不打扰用户。
                _logger.Warn("DPAPI 解密失败（会话文件已失效）: " + ex.Message);
                return null;
            }
        }

        if (protectedText.StartsWith(PlainPrefix, StringComparison.Ordinal))
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[PlainPrefix.Length..]));
            }
            catch (FormatException ex)
            {
                _logger.Warn("会话密文 Base64 解析失败: " + ex.Message);
                return null;
            }
        }

        _logger.Warn("会话密文没有可识别的前缀，忽略。");
        return null;
    }

    private static bool Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var sample = Encoding.UTF8.GetBytes("probe");
            var encrypted = Encrypt(sample);
            return Decrypt(encrypted).AsSpan().SequenceEqual(sample);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or CryptographicException or OutOfMemoryException)
        {
            return false;
        }
    }

    private static byte[] Encrypt(byte[] plaintext)
    {
        var input = new DataBlob { Size = plaintext.Length, Data = Marshal.AllocHGlobal(plaintext.Length) };
        Marshal.Copy(plaintext, 0, input.Data, plaintext.Length);
        var entropy = new DataBlob { Size = Entropy.Length, Data = Marshal.AllocHGlobal(Entropy.Length) };
        Marshal.Copy(Entropy, 0, entropy.Data, Entropy.Length);
        try
        {
            if (!CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, 0, out var output))
            {
                throw new InvalidOperationException("CryptProtectData 返回失败，错误码 " + Marshal.GetLastWin32Error());
            }

            try
            {
                return CopyOut(output);
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            Marshal.FreeHGlobal(entropy.Data);
        }
    }

    private static byte[] Decrypt(byte[] ciphertext)
    {
        var input = new DataBlob { Size = ciphertext.Length, Data = Marshal.AllocHGlobal(ciphertext.Length) };
        Marshal.Copy(ciphertext, 0, input.Data, ciphertext.Length);
        var entropy = new DataBlob { Size = Entropy.Length, Data = Marshal.AllocHGlobal(Entropy.Length) };
        Marshal.Copy(Entropy, 0, entropy.Data, Entropy.Length);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 0, out var output))
            {
                throw new InvalidOperationException("CryptUnprotectData 返回失败，错误码 " + Marshal.GetLastWin32Error());
            }

            try
            {
                return CopyOut(output);
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            Marshal.FreeHGlobal(entropy.Data);
        }
    }

    private static byte[] CopyOut(DataBlob blob)
    {
        var result = new byte[blob.Size];
        Marshal.Copy(blob.Data, result, 0, blob.Size);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
