namespace PdxLauncher.Core.Abstractions;

/// <summary>
/// 敏感字符串的加解密。<c>Core</c> 只定义契约：Windows 上是 DPAPI（<c>CryptProtectData</c>，
/// 与当前用户绑定），自检注入假实现，其它平台可退化为明文。
/// </summary>
public interface ISecretProtector
{
    /// <summary>人可读的算法名，写进文件头与日志（如 <c>dpapi</c> / <c>plain</c>）。</summary>
    string Scheme { get; }

    /// <summary>把明文保护的载荷转成可落盘的字符串。</summary>
    string Protect(string plaintext);

    /// <summary>
    /// 还原 <see cref="Protect"/> 的结果。密文损坏、换了用户或换了机器时返回 null，
    /// <b>不抛异常</b> —— 解不开等价于「没有会话」，让用户重新登录即可。
    /// </summary>
    string? Unprotect(string protectedText);
}
