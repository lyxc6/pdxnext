using System.Collections.Generic;
using System.Text;
using PDX.SDK.Contracts.Internal;

namespace PDX.SDK.Network.Models;

public class Request
{
	public List<Header> Headers;

	public byte[] Body;

	public FlowData FlowData;

	public int Timeout;

	public Request()
	{
		Headers = new List<Header>();
		Body = null;
		FlowData = null;
		Timeout = 0;
	}

	public void SetBody(byte[] body)
	{
		Body = body;
	}

	public void SetBody(string body, Encoding encoding = null)
	{
		SetBody((encoding == null) ? Encoding.UTF8.GetBytes(body) : encoding.GetBytes(body));
	}
}
