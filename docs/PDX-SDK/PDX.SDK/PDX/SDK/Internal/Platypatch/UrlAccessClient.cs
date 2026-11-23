using System;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Exceptions;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Platypatch;

internal class UrlAccessClient
{
	private readonly TimeSpan _refreshThreshold = TimeSpan.FromMinutes(1.0);

	private readonly string _repositoryName;

	private readonly IRequestBuilderAndResponseParserFactory _requestBuilderAndResponseParserFactory;

	private PatchRepositoryResponse _lastResponse;

	public UrlAccessClient(string repositoryName, IRequestBuilderAndResponseParserFactory requestBuilderAndResponseParserFactory)
	{
		_repositoryName = repositoryName;
		_requestBuilderAndResponseParserFactory = requestBuilderAndResponseParserFactory;
	}

	public async Task<string> GetUrl(string relativePath, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		if (NeedsRefresh(flowData2))
		{
			await RefreshAccess(flowData2).ConfigureAwait(continueOnCapturedContext: false);
		}
		return _lastResponse.Prepend.EndsWith("/") ? (_lastResponse.Prepend + relativePath + _lastResponse.Append) : (_lastResponse.Prepend + "/" + relativePath + _lastResponse.Append);
	}

	private bool NeedsRefresh(FlowData flowData = null)
	{
		new FlowData(flowData);
		if (_lastResponse == null)
		{
			return true;
		}
		long num = Convert.ToInt64(_lastResponse.Ttl);
		DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(num);
		return DateTime.UtcNow >= dateTime - _refreshThreshold;
	}

	private async Task RefreshAccess(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Response response = await _requestBuilderAndResponseParserFactory.CreateRequestBuilder().SetRestMethod(RESTMethod.GET).SetPath("patch/repo/" + _repositoryName + "/")
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false);
		PDXResponseParser<PatchRepositoryResponse> pDXResponseParser = _requestBuilderAndResponseParserFactory.CreateResponseParser<PatchRepositoryResponse>(response, fd);
		Result resultObject = pDXResponseParser.GetResultObject();
		if (!resultObject.Success)
		{
			throw new ExceptionWithSubCategory(resultObject.Error.SubCategory, $"Exception while trying to refresh access to signed URL, category: {resultObject.Error?.Category} | sub-category: {resultObject.Error?.SubCategory} | error: {resultObject.Error?.Details}");
		}
		_lastResponse = pDXResponseParser.GetResponse();
	}
}
