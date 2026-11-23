using System.Security.Cryptography;
using System.Text;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Credential.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderEmailPasswordExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, EmailAndPasswordCredential credential)
	{
		byte[] array = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(credential.Password + "64DC4DC6508F4A9D823179D4D99DB4EE"));
		StringBuilder stringBuilder = new StringBuilder();
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2", Defaults.Culture));
		}
		return builder.SetAuthHeader(new HawkAuthModel(credential.Email, stringBuilder.ToString()).ToJson());
	}
}
