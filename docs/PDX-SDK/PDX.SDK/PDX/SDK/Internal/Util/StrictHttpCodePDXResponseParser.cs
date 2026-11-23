using PDX.SDK.Contracts.Internal;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Util;

internal class StrictHttpCodePDXResponseParser<T> : PDXResponseParser<T> where T : HTTPBodyBase
{
	internal StrictHttpCodePDXResponseParser(Context context, Response response, bool hasJSONBody = true, FlowData fd = null)
		: base(context, response, hasJSONBody, fd)
	{
	}

	public override bool IsSuccess()
	{
		Response httpResponse = _httpResponse;
		if (httpResponse != null && httpResponse.StatusCode == 200)
		{
			return true;
		}
		return base.IsSuccess();
	}
}
internal class StrictHttpCodePDXResponseParser : PDXResponseParser
{
	internal StrictHttpCodePDXResponseParser(Context context, Response response, bool hasJSONBody = true, FlowData fd = null)
		: base(context, response, hasJSONBody, fd)
	{
	}

	public override bool IsSuccess()
	{
		Response httpResponse = _httpResponse;
		if (httpResponse != null && httpResponse.StatusCode == 200)
		{
			return true;
		}
		return base.IsSuccess();
	}
}
