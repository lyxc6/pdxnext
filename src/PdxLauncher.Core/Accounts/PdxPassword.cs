namespace PdxLauncher.Core.Accounts;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Paradox 账号的密码处理。<b>只有一种算法</b>：<c>sha256(password + salt)</c> 的小写十六进制。
/// <para>
/// 盐与算法来自官方启动器的登录请求头（本机逆向 app.asar 得到，已用错误凭据实测
/// 返回 <c>401 not-authorized</c> 验证协议正确）：
/// <c>Authorization: {"hawk":{"email":"…","sha256(password+salt)":"…"}}</c>。
/// 服务端不接受明文密码，也不接受 bcrypt/PBKDF2 之类的本地变体。
/// </para>
/// </summary>
public static class PdxPassword
{
    /// <summary>
    /// 官方启动器内置的固定盐。它是协议的一部分，不是机密：
    /// 换掉它服务端就会判为凭据错误。
    /// </summary>
    public const string Salt = "64DC4DC6508F4A9D823179D4D99DB4EE";

    /// <summary>
    /// 登录请求头里承载哈希的那一项的键名。它<b>就是这个字符串</b>（官方启动器逐字如此），
    /// 不是 <c>password</c> 或 <c>hash</c> —— 写错服务端会判为缺字段。
    /// </summary>
    public const string HeaderKeyName = "sha256(password+salt)";

    /// <summary>按官方算法算请求头里那串哈希（小写十六进制）。</summary>
    /// <param name="password">用户输入的明文密码；调用方负责用完就丢。</param>
    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var bytes = Encoding.UTF8.GetBytes(password + Salt);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
