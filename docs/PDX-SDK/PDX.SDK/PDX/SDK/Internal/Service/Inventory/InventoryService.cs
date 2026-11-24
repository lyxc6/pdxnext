using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Inventory;
using PDX.SDK.Contracts.Service.Inventory.Result;
using PDX.SDK.Internal.Service.Inventory.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;

namespace PDX.SDK.Internal.Service.Inventory;

internal class InventoryService : InternalService, IInventoryService
{
	internal InventoryService(Context context)
		: base("Inventory", context)
	{
	}

	public Task<InventoryItemsResult> GetInventoryItems()
	{
		return GetInventoryItems(_context.Namespace);
	}

	internal async Task<InventoryItemsResult> GetInventoryItems(string @namespace, FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<InventoryItemsResult>("retrieve a items list");
		}
		FlowData flowData2 = new FlowData();
		Log("Inventory.GetInventoryItems", LogLevel.L1_Debug, flowData2);
		PDXResponseParser<GetInventoryItemsResponse> pDXResponseParser = CreateResponseParser<GetInventoryItemsResponse>(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.GET).SetPath("inventory").SetAuthHeader()
			.AddParameterToPath("namespace", @namespace)
			.AddParameterToPath("category", "items")
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), flowData);
		GetInventoryItemsResponse response = pDXResponseParser.GetResponse();
		InventoryItemsResult resultObject = pDXResponseParser.GetResultObject<InventoryItemsResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		if (response.items.Count > 0 && response.items[@namespace].Count > 0)
		{
			resultObject.InventoryItems = new Dictionary<string, string>(response.items[@namespace]);
		}
		else
		{
			resultObject.InventoryItems = new Dictionary<string, string>();
		}
		return resultObject;
	}
}
