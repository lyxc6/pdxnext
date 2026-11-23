using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal interface IRequestBuilderAndResponseParserFactory
{
	RequestBuilder CreateRequestBuilder(FlowData fd = null);

	PDXResponseParser<T> CreateResponseParser<T>(Response response, FlowData fd = null, bool hasJSONBody = true) where T : HTTPBodyBase;

	PDXResponseParser CreateResponseParser(Response response, FlowData fd = null, bool hasJSONBody = true);
}
