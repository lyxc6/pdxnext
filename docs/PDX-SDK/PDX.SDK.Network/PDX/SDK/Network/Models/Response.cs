using System.Collections.Generic;
using System.Text;

namespace PDX.SDK.Network.Models;

public class Response
{
	public Dictionary<string, string> Headers;

	public byte[] Body;

	public int StatusCode;

	public Response()
	{
		Headers = new Dictionary<string, string>();
		Body = new byte[1];
		StatusCode = -1;
	}

	public static Response CreateErrorResponse(string errorBody, int? statusCode = null)
	{
		return new Response
		{
			StatusCode = (statusCode ?? 418),
			Headers = new Dictionary<string, string>(),
			Body = Encoding.UTF8.GetBytes(errorBody)
		};
	}
}
