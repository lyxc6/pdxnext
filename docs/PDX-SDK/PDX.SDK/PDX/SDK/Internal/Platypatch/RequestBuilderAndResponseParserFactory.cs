using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Platypatch;

internal class RequestBuilderAndResponseParserFactory : IRequestBuilderAndResponseParserFactory
{
	private readonly Context _context;

	public RequestBuilderAndResponseParserFactory(Context context)
	{
		_context = context;
	}

	public RequestBuilder CreateRequestBuilder(FlowData fd = null)
	{
		return new RequestBuilder(_context, fd);
	}

	public PDXResponseParser<T> CreateResponseParser<T>(Response response, FlowData fd = null, bool hasJSONBody = true) where T : HTTPBodyBase
	{
		return new PDXResponseParser<T>(_context, response, hasJSONBody, fd);
	}

	public PDXResponseParser CreateResponseParser(Response response, FlowData fd = null, bool hasJSONBody = true)
	{
		return new PDXResponseParser(_context, response, hasJSONBody, fd);
	}
}
